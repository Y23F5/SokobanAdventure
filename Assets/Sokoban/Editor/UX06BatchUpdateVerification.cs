/**
 * @file UX06BatchUpdateVerification.cs
 * @brief 通过公共草稿接口重选两墙并批量修改条件，逐项对比 Core 行为图、选择及撤销重做后的整房编码。Check 记录逐项结果。
 * Check(ok, detail)：断言失败抛出 InvalidOperationException，成功写入 UX06BATCH PASS 日志。
 * Run()：菜单入口，依次验证两墙默认图、批量更新、单次撤销与重做、单格重排和再次批量更新。
 * Describe(graph)：把图的 ID、所有者、节点（ID、类型、排序后的参数）与连线拼成可比较的字符串。
 * WithCondition(graph, value)：返回条件节点 enabled 参数被改写的新图，其余节点与连线原样保留。
 * GraphOwnedBy(draft, owner)：在草稿模型的行为图中取指定所有者的唯一一张图。
 * ConditionValue(graph)：读取图中唯一条件节点的 enabled 参数。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class UX06BatchUpdateVerification
    {
        static void Check(bool ok, string detail)
        {
            if (!ok) throw new InvalidOperationException(detail);
            Debug.Log("UX06BATCH PASS " + detail);
        }

        static string Describe(BehaviorGraph graph) =>
            graph.Id + "|" + graph.OwnerId + "|"
            + string.Join(";", graph.Nodes.Select(n => n.Id + ":" + n.Kind + ":"
                + string.Join(",", n.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)))) + "|"
            + string.Join(";", graph.Edges.Select(e => e.FromNode + "." + e.FromPort + ">" + e.ToNode + "." + e.ToPort));

        static BehaviorGraph WithCondition(BehaviorGraph graph, string value) =>
            new BehaviorGraph(graph.Id, graph.OwnerId,
                graph.Nodes.Select(n => n.Kind == ObjectBehavior.Condition ? ObjectBehavior.WithParameter(n, "enabled", value) : n).ToList(),
                graph.Edges);

        static BehaviorGraph GraphOwnedBy(RoomDraft draft, string owner) => draft.Model.Graphs.Single(g => g.OwnerId == owner);

        static string ConditionValue(BehaviorGraph graph) =>
            ObjectBehavior.Parameter(graph.Nodes.Single(n => n.Kind == ObjectBehavior.Condition), "enabled");

        [MenuItem("Sokoban/验证/UX06-002 两墙重复批量更新")]
        public static void Run()
        {
            var draft = new LayoutPanel().Draft;
            var a = new Cell(2, 0);
            var b = new Cell(3, 0);
            var walls = new[] { a, b };
            Check(walls.All(p => draft.Room.TerrainAt(p).CategoryId == TerrainCategories.Wall), "two wall cells");
            draft.Select(a);
            draft.Select(b, true);
            Check(draft.Selection.SetEquals(walls), "两墙同时被选中");
            draft.SetBehavior(b, ObjectBehavior.Template());
            string ownerA = draft.OwnerAt(a), ownerB = draft.OwnerAt(b);
            Check(draft.Selection.SetEquals(walls) && ownerA != null && ownerB != null && ownerA != ownerB, "批量写入后两墙各有独立地形实体 ID，选择保持");
            var initial = draft.Model.Graphs.ToList();
            Check(initial.Count == 2 && walls.All(p => initial.Count(g => g.OwnerId == draft.OwnerAt(p)) == 1), "one bound graph per wall");
            foreach (var graph in initial) Check(ConditionValue(graph) == "true", "initial condition enabled on " + graph.Id);
            var initialIds = initial.Select(g => g.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();

            var before = RoomCodec.Encode(draft.Model);
            var secondaryBefore = GraphOwnedBy(draft, ownerA);
            var primary = WithCondition(GraphOwnedBy(draft, ownerB), "false");
            draft.Select(a);
            draft.Select(b, true);
            draft.SetBehavior(b, primary);
            var after = RoomCodec.Encode(draft.Model);
            var graphA = GraphOwnedBy(draft, ownerA);
            var graphB = GraphOwnedBy(draft, ownerB);
            Check(draft.Model.Graphs.Count == 2 && draft.Model.Graphs.Select(g => g.Id).OrderBy(i => i, StringComparer.Ordinal).SequenceEqual(initialIds), "graph count, owners and graph IDs preserved");
            Check(Describe(graphB) == Describe(primary), "当前格的图与提交的图逐项一致，节点 ID 与连线保留");
            Check(ConditionValue(graphA) == "false" && graphA.Nodes.Select(n => n.Kind).SequenceEqual(secondaryBefore.Nodes.Select(n => n.Kind)) && graphA.Edges.Count == secondaryBefore.Edges.Count, "另一面墙的条件同批关闭，节点类型顺序与连线数量保持");
            Check(draft.Selection.SetEquals(walls), "批量更新后两墙仍被选中");
            Check(after != before, "批量更新改变了房间编码");
            draft.Undo();
            Check(RoomCodec.Encode(draft.Model) == before, "one undo restores both original graphs");
            Check(draft.Selection.Count == 0 && draft.Selected == null, "撤销后清除选择");
            draft.Redo();
            Check(RoomCodec.Encode(draft.Model) == after, "one redo restores both modified graphs");
            Check(draft.Selection.Count == 0 && draft.Selected == null, "重做后清除选择");

            var current = GraphOwnedBy(draft, ownerA);
            var reordered = current.Nodes.Skip(1).Concat(current.Nodes.Take(1)).ToList();
            var untouched = Describe(GraphOwnedBy(draft, ownerB));
            draft.Select(a);
            draft.SetBehavior(a, new BehaviorGraph(current.Id, ownerA, reordered, current.Edges));
            var updated = GraphOwnedBy(draft, ownerA);
            Check(updated.Nodes.Select(n => n.Id).SequenceEqual(reordered.Select(n => n.Id)), "independently reordered nodes keep their IDs and order after single update");
            Check(Describe(GraphOwnedBy(draft, ownerB)) == untouched, "未选中的墙图保持不变");

            draft.Select(a);
            draft.Select(b, true);
            var enabled = WithCondition(GraphOwnedBy(draft, ownerB), "true");
            draft.SetBehavior(b, enabled);
            var batchA = GraphOwnedBy(draft, ownerA);
            Check(batchA.Id == current.Id && batchA.OwnerId == ownerA && ConditionValue(batchA) == "true" && Describe(GraphOwnedBy(draft, ownerB)) == Describe(enabled), "再次批量更新保留两张图的 ID 与所有者并同时开启条件");
            Check(batchA.Nodes.Select(n => n.Id).Distinct().Count() == batchA.Nodes.Count && batchA.Nodes.Select(n => n.Kind).SequenceEqual(enabled.Nodes.Select(n => n.Kind)), "批量写入的副本节点 ID 唯一，类型顺序与当前格图一致");
        }
    }
}
