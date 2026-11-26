/**
 * @file WS06RelockReferenceVerification.cs
 * @brief 重锁、跨房引用与行为图端口的编辑器验证，独立执行三个用例并汇总失败数。
 * Folder：证据目录，可由环境变量 WS06_RELOCK_EVIDENCE 覆盖。Check(ok, label)：断言失败抛异常，通过则输出日志。
 * Campaign()：打开正式世界草稿并切到仓库房。Encoded(model)：以 RoomCodec.Encode 序列化房间，供逐字比较。
 * Describe(graph)：把行为图的 ID、所有者、节点参数与连线写成可比较的字符串。
 * ReplayRoute(run, route)：依照固定方向序列逐步调用 Step，拒绝时记录房间、玩家格与步号。
 * Leave(run)：确认玩家已到出口且房间已解开，随后沿门的外向跨房。
 * Relock()：回放正式三房并核对终点通行；记录工厂出口存档，验证仓库推离目标后重锁、EverSolved 保留，以及有效和失效存档的跨房行为。
 * Reference()：绑定指向工厂箱子的跨房图，擦除遇对象整笔拒绝，RemoveObject 后用 TargetIssues 报出悬空目标，撤销还原，导出再导入后引用仍解析到原对象。
 * Ports()：用 ObjectBehavior.GraphIssues 检查合法分支与无效输出、输入端口。
 * Run()：依次执行三个用例，累计失败数，有失败则抛异常。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class WS06RelockReferenceVerification
    {
        static string Folder => Environment.GetEnvironmentVariable("WS06_RELOCK_EVIDENCE") ?? Path.GetFullPath("Logs/RelockReference");

        static void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); Debug.Log("GATEQA PASS " + label); }

        static RoomDraft Campaign() { var d = new LayoutPanel().Draft; d.Open(Path.Combine(Application.streamingAssetsPath, "World/main.world.json"), false); d.UseRoom("warehouse"); return d; }

        static string Encoded(RoomDraftModel model) => RoomCodec.Encode(model);

        static string Describe(BehaviorGraph graph) =>
            graph.Id + "/" + graph.OwnerId + "|" + string.Join("|", graph.Nodes.Select(n => n.Id + ":" + n.Kind + ":" + string.Join(",", n.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)))
                .Concat(graph.Edges.Select(e => e.FromNode + "." + e.FromPort + ">" + e.ToNode + "." + e.ToPort)));

        internal static void ReplayRoute(PuzzleRun run, string route)
        {
            var directions = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            for (int i = 0; i < route.Length; i++)
            {
                if (!run.Step(directions["URDL".IndexOf(route[i])], out _))
                    throw new InvalidOperationException($"{run.ActiveId} 第 {i + 1} 步 {route[i]} 被拒绝，玩家格 {run.PlayerLocal}");
            }
        }

        internal const string WarehouseRouteToExit = "RRRURRR";

        static void Leave(PuzzleRun run)
        {
            var door = run.Room.Doors.Single(d => d.Id == "exit");
            Check(run.Solved && run.PlayerLocal == door.Cell, run.ActiveId + " solved and at exit");
            if (!run.Step(new Vector2Int(door.Outward.X, door.Outward.Y), out _)) throw new InvalidOperationException(run.ActiveId + " 出口跨房被拒绝");
        }

        static void Relock()
        {
            var draft = Campaign();
            var run = PuzzleRun.FromDraft(draft);
            ReplayRoute(run, WarehouseRouteToExit);
            Leave(run);
            Check(run.ActiveId == "factory", "solved warehouse crosses into factory");
            ReplayRoute(run, "RURRRRRRLDLDDLUULURRRDLDDRRRRUUR");
            var factoryExit = run.Room.Doors.Single(d => d.Id == "exit");
            Check(run.Solved && run.PlayerLocal == factoryExit.Cell, "factory solved and at exit");
            var atDoor = run.Snapshot();
            Check(run.Step(new Vector2Int(factoryExit.Outward.X, factoryExit.Outward.Y), out _) && run.ActiveId == "forest", "solved factory with solved warehouse crosses to forest");
            ReplayRoute(run, "RURR");
            Check(!run.Step(Vector2Int.right, out _), "closed forest vine blocks passage");
            Check(run.BeginRecovery(), "forest recovery opens the vine");
            ReplayRoute(run, "RR");
            Check(run.PlayerLocal == new Cell(5, 5) && run.AdvanceRecovery(60f), "forest recovery ends after crossing the vine");
            ReplayRoute(run, "RRRRDRULLLLDDDRRRURDURR");
            var finish = run.Room.Doors.Single(d => d.Id == "exit");
            Check(run.CanLeaveWorld(new Vector2Int(finish.Outward.X, finish.Outward.Y)), "replayed room routes reach finish and allow outward step");
            var source = PuzzleRun.FromDraft(draft);
            var gate = source.Room.Doors.Single(d => d.Id == "exit");
            Check(!source.CanApproach(gate), "warehouse initially locked");
            for (int i = 0; i < 3; i++) Check(source.Step(Vector2Int.right, out _), "warehouse right step " + (i + 1));
            Check(source.Solved && source.CanApproach(gate), "warehouse box on target unlocks exit");
            Check(source.Step(Vector2Int.right, out var moved) && moved == "box-1" && source.EverSolved && !source.Solved && !source.CanApproach(gate), "push off target relocks warehouse, everSolved=true");
            run.Restore(atDoor);
            Check(run.ActiveId == "factory" && run.Solved && run.Step(Vector2Int.right, out _) && run.ActiveId == "forest", "restored factory snapshot with solved warehouse crosses to forest");
            run.Restore(atDoor);
            var broken = run.Snapshot();
            var relocked = source.Snapshot();
            broken.Boxes["warehouse"] = relocked.Boxes["warehouse"];
            broken.EverSolved["warehouse"] = relocked.EverSolved["warehouse"];
            File.WriteAllText(Path.Combine(Folder, "factory-open.json"), SaveCodec.Encode(atDoor));
            File.WriteAllText(Path.Combine(Folder, "factory-relocked.json"), SaveCodec.Encode(broken));
            run.Restore(broken);
            string before = SaveCodec.Encode(run.Snapshot());
            var exit = run.Room.Doors.Single(d => d.Id == "exit");
            Check(run.Solved && !run.CanApproach(exit) && !run.Step(Vector2Int.right, out _) && before == SaveCodec.Encode(run.Snapshot()), "solved factory with broken warehouse refuses forest crossing and preserves snapshot");
        }

        static void Reference()
        {
            var draft = Campaign();
            var template = ObjectBehavior.Template();
            var nodes = template.Nodes.Select(n => n.Kind == ObjectBehavior.SetVisible
                ? ObjectBehavior.WithParameter(ObjectBehavior.WithParameter(n, "targetRoom", "factory"), "targetObject", "box-1") : n).ToList();
            string nodeId = nodes.Single(n => n.Kind == ObjectBehavior.SetVisible).Id;
            draft.SetBehavior(new Cell(2, 0), new BehaviorGraph(template.Id, template.OwnerId, nodes, template.Edges));
            var warehouse = draft.Catalog["warehouse"];
            var bound = warehouse.Graphs.Single();
            string boundText = Describe(bound);
            draft.Save(Path.Combine(Folder, "before.world.json"), true);
            draft.UseRoom("factory");
            var target = draft.Find("box-1");
            string factoryBefore = Encoded(draft.Model), warehouseBefore = Encoded(warehouse);
            Check(draft.Brush(new[] { target.Cell }, RoomTool.Erase) != null && draft.Find("box-1") != null && Encoded(draft.Model) == factoryBefore, "erase over an object is refused and leaves the draft unchanged");
            draft.RemoveObject("box-1");
            Check(draft.Find("box-1") == null, "remove deletes actual target box");
            Check(ReferenceEquals(bound, warehouse.Graphs.Single()) && Encoded(warehouse) == warehouseBefore, "source graph retains cross-room reference after removal");
            Func<string, RoomDraftModel> lookup = id => draft.Catalog.TryGetValue(id, out var found) ? found : null;
            var issues = ObjectBehavior.TargetIssues(warehouse, lookup).ToList();
            Check(issues.Count == 1 && issues[0].Contains("factory/box-1") && issues[0].Contains(nodeId), "dangling target reported: " + string.Join(";", issues));
            string refused = Path.Combine(Folder, "refused.world.json");
            if (File.Exists(refused)) File.Delete(refused);
            bool rejected = false;
            string refusal = null;
            try { draft.Save(refused, true); }
            catch (Exception ex) { rejected = true; refusal = ex.Message; }
            Check(rejected && !File.Exists(refused) && refusal != null && refusal.Contains("factory/box-1"), "export with a dangling cross-room target is refused, names the target and writes nothing: " + refusal);
            draft.Undo();
            var back = draft.Find("box-1");
            Check(back != null && back.Cell == target.Cell && back.CategoryId == target.CategoryId && Encoded(draft.Model) == factoryBefore && !ObjectBehavior.TargetIssues(warehouse, lookup).Any(), "single undo restores target identity, properties and clears the dangling report");
            string path = Path.Combine(Folder, "after.world.json");
            draft.Save(path, true);
            draft.Open(path, true);
            draft.UseRoom("factory");
            var restored = draft.Catalog["warehouse"].Graphs.Single();
            var node = restored.Nodes.Single(n => n.Kind == ObjectBehavior.SetVisible);
            var resolved = draft.Find(ObjectBehavior.Parameter(node, "targetObject"));
            Check(ObjectBehavior.Parameter(node, "targetRoom") == draft.ActiveId && resolved != null && resolved.Cell == target.Cell && Describe(restored) == boundText, "reimported action resolves original factory object");
        }

        static void Ports()
        {
            var graph = ObjectBehavior.Template("owner");
            Check(!ObjectBehavior.GraphIssues(graph, null).Any(), "template graph has no port issues");
            var last = graph.Edges.Last();
            var first = graph.Edges.First();
            List<string> Issues(BehaviorEdge edge) =>
                ObjectBehavior.GraphIssues(new BehaviorGraph(graph.Id, graph.OwnerId, graph.Nodes, graph.Edges.Take(graph.Edges.Count - 1).Concat(new[] { edge }).ToList()), null).ToList();
            Check(!Issues(new BehaviorEdge(last.FromNode, "false", last.ToNode, last.ToPort)).Any(), "false branch of condition is valid");
            var badOut = Issues(new BehaviorEdge(last.FromNode, ObjectBehavior.Out, last.ToNode, last.ToPort));
            Check(badOut.Any(i => i.Contains(last.FromNode) && i.Contains(ObjectBehavior.Out)), "invalid output port reported: " + string.Join(";", badOut));
            var badIn = Issues(new BehaviorEdge(last.FromNode, "true", first.FromNode, ObjectBehavior.In));
            Check(badIn.Any(i => i.Contains(first.FromNode) && i.Contains(ObjectBehavior.In)), "invalid input port reported: " + string.Join(";", badIn));
        }

        public static void Run()
        {
            Directory.CreateDirectory(Folder);
            int errors = 0;
            foreach (Action action in new Action[] { Relock, Reference, Ports })
                try { action(); }
                catch (Exception ex) { errors++; Debug.LogError("GATEQA FAIL " + action.Method.Name + " " + ex); }
            Debug.Log("GATEQA failures=" + errors);
            if (errors != 0) throw new Exception("verification failed");
        }
    }
}
