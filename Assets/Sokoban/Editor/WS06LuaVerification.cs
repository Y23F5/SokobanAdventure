/**
 * @file WS06LuaVerification.cs
 * @brief 验证行为图里 Lua 条件与动作节点的执行结果、表达式类型约束、执行预算，以及求解器对不可模拟节点的分类，数据一律用 Core.BehaviorGraph 与 TriggerIds。
 * Run：从默认房间建一张含 Lua 节点的行为图，逐项断言，失败抛 InvalidOperationException。
 * Check、Reject：记录通过的断言，或要求预期的诊断文字出现。
 * Rebuild、Replace：行为节点不可变，按节点映射生成同 ID 的新图，或只替换某一类节点。
 * Visible：读取 setVisible 节点的 visible 参数文本。
 * Unsupported：断言草稿内含不可模拟节点，且求解结论为无法验证、展开数为 0。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Sokoban.Core;

namespace Sokoban.Editor
{
    public static class WS06LuaVerification
    {
        static void Check(bool ok, string detail)
        {
            if (!ok) throw new InvalidOperationException(detail);
            Debug.Log("WS06LUA PASS " + detail);
        }

        static void Reject(Action action, string message)
        {
            try { action(); }
            catch (InvalidOperationException ex) when (ex.Message.Contains(message)) { Check(true, ex.Message); return; }
            throw new InvalidOperationException("缺少预期拒绝 " + message);
        }

        static BehaviorGraph Rebuild(BehaviorGraph graph, string ownerId, Func<BehaviorNode, BehaviorNode> map) =>
            new BehaviorGraph(graph.Id, ownerId, graph.Nodes.Select(map).ToList(), graph.Edges);

        static Func<BehaviorNode, BehaviorNode> Replace(string kind, Func<BehaviorNode, BehaviorNode> change) =>
            node => node.Kind == kind ? change(node) : node;

        static string Visible(BehaviorNode node) => ObjectBehavior.Parameter(node, "visible");

        public static void Run()
        {
            var draft = new LayoutPanel().Draft;
            var cell = new Cell(3, 0);
            draft.SetBehavior(cell, ObjectBehavior.Template());
            string owner = draft.OwnerAt(cell);
            string expression = "self.id == '" + owner + "'";
            var luaAction = new Dictionary<string, string> { ["script"] = "self.setVisible(false); self.setVisible(true)" };
            var graph = Rebuild(draft.Model.Graphs.Single(), owner, node =>
                node.Kind == ObjectBehavior.Condition ? ObjectBehavior.WithParameter(node, "expression", expression)
                : node.Kind == ObjectBehavior.SetVisible ? new BehaviorNode(node.Id, ObjectBehavior.Lua, node.Cell, luaAction) : node);
            draft.SetBehavior(cell, graph);
            graph = draft.Model.Graphs.Single();
            var effects = ObjectBehavior.Effects(graph, TriggerIds.RoomSolved).ToArray();
            Check(effects.Length == 2 && effects.All(e => e.Kind == ObjectBehavior.SetVisible) && Visible(effects[0]) == "false" && Visible(effects[1]) == "true",
                "matching self.id runs self.setVisible(false), then true");
            Check(!ObjectBehavior.Effects(Rebuild(graph, "different-owner", n => n), TriggerIds.RoomSolved).Any(), "different self.id skips action");
            Reject(() => BehaviorLua.Evaluate("self.id", owner, true, null), "布尔");
            BehaviorLua.Evaluate("local n=0; for i=1,5 do n=n+i end", owner, false, null);
            Reject(() => BehaviorLua.Evaluate("local n=0; for i=1,100000 do n=n+i end", owner, false, null), "预算");
            Check(BehaviorLua.Evaluate("self.id ~= ''", owner, true, null), "boolean condition succeeds after budget rejection");

            var start = draft.Room.ObjectsOf(ObjectCategories.Spawn).Single().Cell;
            void Unsupported(string label)
            {
                var model = draft.Model;
                var result = RoomSolution.Check(model, start, 100000, BehaviorNodeKinds.HasUnsimulatable(ObjectBehavior.AllGraphs(model)));
                Check(result.Verdict == SolveVerdict.Unverifiable && result.Expanded == 0, label + " " + RoomSolution.Label(result.Verdict) + " expanded=" + result.Expanded);
            }

            draft.SetBehavior(cell, Rebuild(graph, owner, Replace(ObjectBehavior.Condition, n => ObjectBehavior.WithParameter(n, "expression", null))));
            Unsupported("Lua action alone");
            var plain = Rebuild(draft.Model.Graphs.Single(), owner, n =>
                n.Kind == ObjectBehavior.Lua ? ObjectBehavior.Node(ObjectBehavior.SetVisible, new Dictionary<string, string> { ["visible"] = "false" }, n.Id, n.Cell) : n);
            draft.SetBehavior(cell, plain);
            Check(!BehaviorNodeKinds.HasUnsimulatable(ObjectBehavior.AllGraphs(draft.Model)), "plain setVisible graph stays simulatable");
            draft.SetBehavior(cell, Rebuild(plain, owner, Replace(ObjectBehavior.Condition, n => ObjectBehavior.WithParameter(n, "expression", "true"))));
            Unsupported("Lua expression alone");
        }
    }
}
