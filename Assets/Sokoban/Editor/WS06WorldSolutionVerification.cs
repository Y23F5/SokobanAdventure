/**
 * @file WS06WorldSolutionVerification.cs
 * @brief 世界求解的编辑器回归：用正式三房与 Blueprint 构造的双房夹具核对 WorldSolution 的四种结论、预算计数与输入不被修改，并用真实 PuzzleRun 步进回放确认有解的两房路线。
 * Run：菜单与批处理入口，逐例执行并汇总失败。
 * Room、World、Pair：由字符画图纸构造带恢复道具的房间、世界文档（A 的出口直连 B 的入口，终点为 B 的出口）与房间表。
 * Verify：调用 WorldSolution.Check，核对结论与路线文字，并确认世界文档与各房间编码前后一致。
 * Replay：夹具写成世界文件后经 RoomDraft 重开，逐房求解、真实步进、走到出口跨房，最后核对 CanLeaveWorld。
 * SolveInRoom、WalkTo：内部辅助，对活动房间求解并步进，以及沿可通行格走到指定格，GridPlayVerification 复用。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Sokoban.Core;

namespace Sokoban.Editor
{
    public static class WS06WorldSolutionVerification
    {
        static readonly string[] Plain = { "#########", "#.......#", "<.P.B.T.>", "#########" };

        static void Require(bool value, string detail)
        {
            if (!value) throw new InvalidOperationException(detail);
        }

        static RoomDraftModel Room(string id, string[] rows)
        {
            var parsed = Blueprint.Parse(id, rows);
            return new RoomDraftModel(parsed.Room, parsed.Doors, "entry", null, new[] { new RecoveryItem("recovery-1", "warehouse-lift", 3.7f) });
        }

        static WorldDocument World(bool connected) => new WorldDocument("qa-world", new Cell(0, 0), new Cell(63, 31),
            new[] { new WorldRoomRef("A", "", new Cell(0, 0), new string[0]), new WorldRoomRef("B", "", new Cell(9, 0), new[] { "A" }) },
            connected ? new[] { new WorldConnection("AB", WorldCodec.Direct, new WorldEndpoint("A", "exit"), new WorldEndpoint("B", "entry"), true) } : new WorldConnection[0],
            new WorldEndpoint("A", "spawn"), new WorldEndpoint("B", "exit"), new[] { "A", "B" });

        static Dictionary<string, RoomDraftModel> Pair(string[] second) =>
            new Dictionary<string, RoomDraftModel>(StringComparer.Ordinal) { ["A"] = Room("A", Plain), ["B"] = Room("B", second) };

        static RoomSolution.Result Verify(string label, WorldDocument world, IReadOnlyDictionary<string, RoomDraftModel> models, int budget, SolveVerdict expected, string detail = null)
        {
            string worldBefore = WorldCodec.Encode(world);
            var roomsBefore = models.ToDictionary(p => p.Key, p => RoomCodec.Encode(p.Value));
            var result = WorldSolution.Check(world, models, budget);
            Debug.Log($"WORLDQA OBS {label} budget={budget} verdict={result.Verdict} expanded={result.Expanded} detail={result.Detail}");
            Require(result.Verdict == expected, label + " expected " + expected + " observed " + result.Verdict);
            Require(detail == null || result.Detail == detail, label + " detail " + result.Detail);
            Require(worldBefore == WorldCodec.Encode(world) && models.All(p => roomsBefore[p.Key] == RoomCodec.Encode(p.Value)), label + " input mutated");
            return result;
        }

        internal static void SolveInRoom(PuzzleRun run)
        {
            var result = RoomSolution.Check(run.Models[run.ActiveId], run.PlayerLocal, 100000);
            Require(result.Verdict == SolveVerdict.Confirmed, "房间 " + run.ActiveId + " 未确认有解：" + result.Verdict);
            foreach (var move in result.Moves)
                Require(run.Step(new Vector2Int(move.X, move.Y), out _), "回放被拒绝 " + run.ActiveId + " " + move);
            Require(run.Solved, "回放结束仍未解开 " + run.ActiveId);
        }

        internal static void WalkTo(PuzzleRun run, Cell target)
        {
            var room = run.Room.Room;
            var boxes = new HashSet<Cell>(run.BoxesIn(run.ActiveId).Values.Select(v => new Cell(v.x - run.Origin.x, v.y - run.Origin.y)));
            var parent = new Dictionary<Cell, Cell>();
            var queue = new Queue<Cell>();
            var start = run.PlayerLocal;
            parent[start] = start;
            queue.Enqueue(start);
            while (queue.Count > 0 && !parent.ContainsKey(target))
            {
                var cell = queue.Dequeue();
                foreach (var direction in Cell.SearchOrder)
                {
                    var next = cell + direction;
                    string category = room.TerrainAt(next).CategoryId;
                    if (parent.ContainsKey(next) || boxes.Contains(next)) continue;
                    if (category != TerrainCategories.Floor && category != TerrainCategories.Door) continue;
                    parent[next] = cell;
                    queue.Enqueue(next);
                }
            }
            Require(parent.ContainsKey(target), "走不到 " + target);
            var path = new List<Cell>();
            for (var cell = target; cell != start; cell = parent[cell]) path.Add(cell);
            path.Reverse();
            var at = start;
            foreach (var cell in path)
            {
                var delta = cell - at;
                Require(run.Step(new Vector2Int(delta.X, delta.Y), out _), "步行被拒绝 " + at + " -> " + cell);
                at = cell;
            }
        }

        static void Replay(string folder)
        {
            var models = Pair(Plain);
            string path = Path.Combine(folder, "two-room.world.json");
            Directory.CreateDirectory(folder);
            WorldLayout.Write(path, World(true), models);
            var draft = new RoomDraft(models["A"]);
            draft.Open(path, false);
            draft.UseRoom("A");
            var run = PuzzleRun.FromDraft(draft);
            Require(run.ActiveId == "A" && !run.Solved, "起点房间或初始状态错误");
            SolveInRoom(run);
            var exitA = run.Room.Doors.Single(d => d.Role == "exit");
            WalkTo(run, exitA.Cell);
            Require(run.Step(new Vector2Int(exitA.Outward.X, exitA.Outward.Y), out _) && run.ActiveId == "B", "A 解开后未能经出口进入 B");
            Require(run.PlayerLocal == run.Room.Doors.Single(d => d.Role == "entry").Cell, "跨房后未落在 B 的入口门格");
            Debug.Log("WORLDQA CROSS A->B");
            SolveInRoom(run);
            var exitB = run.Room.Doors.Single(d => d.Role == "exit");
            WalkTo(run, exitB.Cell);
            Require(run.CanLeaveWorld(new Vector2Int(exitB.Outward.X, exitB.Outward.Y)), "运行时拒绝了终点外迈一步");
            Debug.Log("WORLDQA FINAL " + run.ActiveId + "." + exitB.Id + " player=" + run.PlayerLocal);
        }

        [MenuItem("Sokoban/验证/WS-06 世界求解")]
        public static void Run()
        {
            var folder = Environment.GetEnvironmentVariable("WS06_WORLD_EVIDENCE") ?? Path.GetFullPath("Logs/WS06World");
            var catalog = new Dictionary<string, RoomDraftModel>(StringComparer.Ordinal);
            var campaign = WorldLayout.Read(Path.Combine(Application.streamingAssetsPath, "World/main.world.json"), catalog);
            int passed = 0, total = 0;
            void Case(string name, Action action)
            {
                total++;
                try { action(); passed++; Debug.Log("WORLDQA PASS " + name); }
                catch (Exception ex) { Debug.LogError("WORLDQA FAIL " + name + " " + ex); }
            }
            Case("budget-zero", () => Require(Verify("budget-zero", campaign, catalog, 0, SolveVerdict.Unverifiable).Expanded == 0, "零预算仍展开状态"));
            Case("budget-one", () => Require(Verify("budget-one", campaign, catalog, 1, SolveVerdict.Unverifiable).Expanded == 1, "预算计数不符"));
            Case("campaign-optimistic", () => Verify("campaign", campaign, catalog, 100000, SolveVerdict.Optimistic, "warehouse → factory → forest"));
            Case("fixture-confirmed", () => Verify("fixture-confirmed", World(true), Pair(Plain), 100000, SolveVerdict.Confirmed, "A → B"));
            Case("fixture-replay", () => Replay(folder));
            Case("fixture-unsolvable", () => Verify("fixture-unsolvable", World(true), Pair(new[] { "#########", "<.P#B.T.>", "#########" }), 100000, SolveVerdict.Unsolvable, "房间 B 确认无解"));
            Case("fixture-vine-optimistic", () => Verify("fixture-vine", World(true), Pair(new[] { "#########", "<.V.B.T.>", "#########" }), 100000, SolveVerdict.Optimistic, "A → B"));
            Case("fixture-unreached", () => Verify("fixture-unreached", World(false), Pair(Plain), 100000, SolveVerdict.Unverifiable, "候选路线未到终点，已通过：A"));
            Debug.Log($"WORLDQA RESULT {passed}/{total}");
            Require(passed == total, "WS06 世界求解回归失败");
        }
    }
}
