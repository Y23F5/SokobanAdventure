/**
 * @file WS06EntryVerification.cs
 * @brief 入口清单、无法验证诊断与矩形框选的编辑器验证，全部走类型化草稿与 Core 求解结论。
 * Rows(label, report, labels)：把 CheckEntries 的多行报告按“起点 · 结论 路线”拆成字典，核对起点标签集合与期望完全一致，缺失、多出或格式错误即抛异常。
 * Replay(draft, start, row)：以 PuzzleRun.FromDraft 建立试玩，经 Snapshot 与 Restore 把玩家改到起点，按“确认有解”行里的 U、R、D、L 逐步调用 Step，要求每步被接受且终局 Solved。
 * PartitionedRoom()：在默认房间放一箱一目标，用整行墙隔出南北两区并在北区开第二入口，要求出生点与西入口确认有解且可回放，北入口确认无解，且每行与单点 Check 的结论和路线一致。
 * Run()：菜单入口，依次执行隔墙房间、单房入口回放、不可模拟节点诊断、世界零预算入口报告与反向越界矩形框选。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sokoban.Core;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class WS06EntryVerification
    {
        static Dictionary<string, string> Rows(string label, string report, params string[] labels)
        {
            Debug.Log("WS06ENT REPORT " + label + " | " + report.Replace('\n', '|'));
            var rows = report.Split('\n').Select(line => line.Split(new[] { " · " }, StringSplitOptions.None)).ToDictionary(
                parts => parts[0],
                parts => parts.Length == 2 ? parts[1] : throw new InvalidOperationException("入口报告格式错误 " + string.Join(" · ", parts)));
            if (!rows.Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(labels.OrderBy(k => k, StringComparer.Ordinal))) throw new InvalidOperationException("入口清单错误 " + label);
            return rows;
        }

        static void Replay(RoomDraft draft, Cell start, string row)
        {
            string prefix = RoomSolution.Label(SolveVerdict.Confirmed) + " ";
            if (!row.StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidOperationException(row);
            var run = PuzzleRun.FromDraft(draft);
            var state = run.Snapshot();
            state.PlayerX = start.X;
            state.PlayerY = start.Y;
            run.Restore(state);
            if (run.PlayerLocal != start) throw new InvalidOperationException("起点还原失败 " + start);
            string route = row.Substring(prefix.Length);
            int solvedAt = -1;
            var deltas = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            for (int i = 0; i < route.Length; i++)
            {
                int index = "URDL".IndexOf(route[i]);
                if (index < 0 || !run.Step(deltas[index], out _)) throw new InvalidOperationException($"第 {i + 1} 步运行拒绝 {route}");
                if (solvedAt < 0 && run.Solved) solvedAt = i + 1;
            }
            if (!run.Solved) throw new InvalidOperationException("路线终局未解开 " + row);
            Debug.Log($"WS06ENT REPLAY start={start} path={route} solvedStep={solvedAt} endStep={route.Length} player={run.PlayerLocal}");
        }

        static void PartitionedRoom()
        {
            var draft = new LayoutPanel().Draft;
            var north = new Cell(3, 6);
            var upperExit = new Cell(5, 6);
            var barrier = Enumerable.Range(0, 7).Select(x => new Cell(x, 4)).ToArray();
            if (draft.Brush(barrier, RoomTool.Wall) != null || draft.Brush(new[] { north, upperExit }, RoomTool.Floor) != null) throw new InvalidOperationException("构造隔墙或开门失败");
            draft.Place(true, new Cell(3, 2));
            draft.Place(false, new Cell(4, 2));
            draft.SetRoomMarker(north, "entry");
            draft.SetRoomMarker(upperExit, "exit");
            var model = draft.Model;
            string northName = "门 " + model.Doors.Single(d => d.Cell == north).Id;
            var rows = Rows("隔墙双入口", RoomSolution.CheckEntries(model, null), "出生点", "门 entry", northName);
            var starts = new Dictionary<string, (Cell start, SolveVerdict verdict)>
            {
                ["出生点"] = (model.Room.ObjectsOf(ObjectCategories.Spawn).First().Cell, SolveVerdict.Confirmed),
                ["门 entry"] = (model.Doors.Single(d => d.Id == "entry").Cell, SolveVerdict.Confirmed),
                [northName] = (north, SolveVerdict.Unsolvable)
            };
            foreach (var pair in starts)
            {
                var result = RoomSolution.Check(model, pair.Value.start);
                if (result.Verdict != pair.Value.verdict || rows[pair.Key] != RoomSolution.Label(pair.Value.verdict) + " " + result.Detail) throw new InvalidOperationException(pair.Key + " " + rows[pair.Key]);
                if (pair.Value.verdict == SolveVerdict.Confirmed) Replay(draft, pair.Value.start, rows[pair.Key]);
            }
            Debug.Log("WS06ENT PASS 隔墙两侧入口结论不同：可推箱一侧确认有解并回放，被隔开的北入口确认无解");
        }

        [MenuItem("Sokoban/验证/WS-06 入口与框选")]
        public static void Run()
        {
            PartitionedRoom();
            var draft = new LayoutPanel().Draft;
            var north = new Cell(3, 6);
            if (draft.Brush(new[] { north }, RoomTool.Floor) != null) throw new InvalidOperationException("无法开门");
            draft.Place(true, new Cell(3, 2));
            draft.Place(false, new Cell(4, 2));
            draft.SetRoomMarker(north, "entry");
            string second = draft.Model.Doors.Single(d => d.Cell == north).Id;
            var rows = Rows("单房", RoomSolution.CheckEntries(draft.Model, null), "出生点", "门 entry", "门 " + second);
            Replay(draft, draft.Model.Room.ObjectsOf(ObjectCategories.Spawn).First().Cell, rows["出生点"]);
            foreach (var door in draft.Model.Doors.Where(d => d.Role == "entry")) Replay(draft, door.Cell, rows["门 " + door.Id]);
            Debug.Log("WS06ENT PASS 两入口与出生点的返回走法分别回放解开");
            var owner = draft.OwnerAt(new Cell(3, 0));
            var script = new BehaviorGraph("unsupported-graph", null,
                new[] { ObjectBehavior.Node(TriggerIds.RoomSolved, null, "start"), ObjectBehavior.Node(ObjectBehavior.Lua, null, "script") },
                new[] { new BehaviorEdge("start", ObjectBehavior.Out, "script", ObjectBehavior.In) });
            draft.SetBehavior(new Cell(3, 0), script);
            owner = draft.OwnerAt(new Cell(3, 0));
            var culprits = ObjectBehavior.AllGraphs(draft.Model).Where(g => !ObjectBehavior.CanSimulate(g)).ToList();
            if (culprits.Count != 1 || culprits[0].OwnerId != owner || culprits[0].Id != "unsupported-graph") throw new InvalidOperationException("不可模拟图缺少所有者定位");
            var unsupported = Rows("无法模拟", RoomSolution.CheckEntries(draft.Model, null, 100000, culprits.Count > 0), rows.Keys.ToArray());
            if (!unsupported.Values.All(v => v.StartsWith(RoomSolution.Label(SolveVerdict.Unverifiable), StringComparison.Ordinal))) throw new InvalidOperationException("入口未报告无法验证");
            Debug.Log("WS06ENT PASS 含不可模拟节点时每个入口报告无法验证，图与所有者可由 AllGraphs 定位");
            var world = new LayoutPanel().Draft;
            world.Open(Path.Combine(Application.streamingAssetsPath, "World", "main.world.json"), false);
            world.UseRoom("factory");
            var links = world.World.Connections;
            var forwardOnly = WorldLayout.WithConnections(world.World, links.Select(c => new WorldConnection(c.Id, c.Mode, c.From, c.To, false)).ToList());
            var withReturn = WorldLayout.WithConnections(world.World, links.Select(c => new WorldConnection(c.Id, c.Mode, c.From, c.To, c.From.LevelId == world.ActiveId || c.AllowReturn)).ToList());
            var forward = Rows("世界单向", RoomSolution.CheckEntries(world.Model, forwardOnly, 0), "出生点", "门 entry");
            var both = Rows("世界回流", RoomSolution.CheckEntries(world.Model, withReturn, 0), "出生点", "门 entry", "门 exit");
            if (!forward.Values.Concat(both.Values).All(v => v.StartsWith(RoomSolution.Label(SolveVerdict.Unverifiable), StringComparison.Ordinal))) throw new InvalidOperationException("零预算端点报告错误");
            Debug.Log("WS06ENT PASS 正向目标端点始终准入，源端点随 allowReturn 进入报告");
            draft.NewBlankRoom();
            var points = new[] { new Cell(0, 0), new Cell(1, 1), new Cell(7, 7) };
            if (draft.Brush(points, RoomTool.Floor) != null) throw new InvalidOperationException("空白房铺地板失败");
            draft.Select(points[2]);
            draft.SelectRectangle(new Cell(1, 1), new Cell(-1, -1));
            if (!draft.Selection.SetEquals(points)) throw new InvalidOperationException("反向框选越界裁剪、跳过 Void 或追加选择失败");
            Debug.Log("WS06ENT PASS Rectangle 反向越界框选保留旧格并跳过空地");
        }
    }
}
