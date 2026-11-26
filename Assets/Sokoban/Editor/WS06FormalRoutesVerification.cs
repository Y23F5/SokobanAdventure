/**
 * @file WS06FormalRoutesVerification.cs
 * @brief 正式三房的起点枚举与求解回归：对 StreamingAssets/World 下的房间与世界文件，逐房逐起点核对 SolveVerdict、CheckEntries 报告行与真实回放，并检查 Lua 动作白名单。
 * Run：菜单与批处理入口，读入世界与房间，对每个起点逐路线验证并汇总。
 * Starts：按世界连接枚举起点，出生点加被连接指向或允许回退的门。
 * Route：含可开合地形的房间只能是乐观有解，其余为确认有解；确认有解的走法经 Solver.Replay 回放，出生点起点再经 PuzzleRun 真实步进；乐观有解的走法不得通过回放。
 * Whitelist：Lua 环境只导出 setVisible，playSound 调用必须被拒绝且不产生任何动作。
 * Require：把验收差异转成批处理失败。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MoonSharp.Interpreter;
using UnityEditor;
using UnityEngine;
using Sokoban.Core;

namespace Sokoban.Editor
{
    public static class WS06FormalRoutesVerification
    {
        static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        static void Whitelist(string owner)
        {
            var effects = new List<BehaviorNode>();
            Require(BehaviorLua.Evaluate("self.playSound == nil", owner, true, effects), "playSound is exported");
            try
            {
                BehaviorLua.Evaluate("self.playSound('chime'); self.setVisible(false)", owner, false, effects);
                throw new InvalidOperationException("playSound returned normally");
            }
            catch (ScriptRuntimeException ex)
            {
                Require(effects.Count == 0, "unexpected effects after rejection");
                Debug.Log("FLOWQA DENIED self.playSound effects=0 error=" + ex.Message);
            }
            BehaviorLua.Evaluate("self.setVisible(false)", owner, false, effects);
            Require(effects.Count == 1 && effects[0].Kind == ObjectBehavior.SetVisible && effects[0].Parameters["visible"] == "false", "positive control failed");
            Debug.Log("FLOWQA ALLOWED self.setVisible(false) effects=1");
        }

        static bool HasOpenable(Room room)
        {
            for (int x = 0; x < room.Width; x++)
                for (int y = 0; y < room.Height; y++)
                    if (room.TerrainAt(new Cell(x, y)).CategoryId == TerrainCategories.Openable) return true;
            return false;
        }

        static Dictionary<string, Cell> Starts(string id, RoomDraftModel model, WorldDocument world)
        {
            var starts = new Dictionary<string, Cell> { ["出生点"] = model.Room.ObjectsOf(ObjectCategories.Spawn).Single().Cell };
            foreach (var link in world.Connections)
            {
                var ends = new List<WorldEndpoint> { link.To };
                if (link.AllowReturn) ends.Add(link.From);
                foreach (var endpoint in ends.Where(e => e.LevelId == id))
                    starts["门 " + endpoint.DoorId] = model.Doors.Single(d => d.Id == endpoint.DoorId).Cell;
            }
            return starts;
        }

        static void StepThrough(RoomDraftModel model, RoomSolution.Result result)
        {
            var run = PuzzleRun.FromDraft(new RoomDraft(model));
            Require(!run.Solved, "initial layout already solved");
            foreach (var move in result.Moves)
                Require(run.Step(new Vector2Int(move.X, move.Y), out _), "rejected step " + move);
            Require(run.Solved && run.Targets.All(t => run.Boxes.ContainsValue(t)), "wrong final state");
        }

        static void Route(string id, RoomDraftModel model, string label, Cell start, string[] report)
        {
            bool vine = HasOpenable(model.Room);
            var expected = vine ? SolveVerdict.Optimistic : SolveVerdict.Confirmed;
            var result = RoomSolution.Check(model, start, 100000);
            Debug.Log($"FLOWQA SEARCH {id} {label} {result.Verdict} expanded={result.Expanded} path={result.Detail}");
            Require(result.Verdict == expected, id + " " + label + " expected " + expected + " observed " + result.Verdict);
            string line = label + " · " + RoomSolution.Label(result.Verdict) + " " + result.Detail;
            Require(report.Count(l => l == line) == 1, "CheckEntries mismatch " + line);
            bool replayed = Solver.Replay(model.Room, start, result.Moves);
            if (vine) Require(!replayed, "乐观有解不应被真实回放确认 " + id);
            else
            {
                Require(replayed, "确认有解的走法未通过真实回放 " + id + " " + label);
                if (label == "出生点") StepThrough(model, result);
            }
        }

        [MenuItem("Sokoban/验证/WS-06 正式三房指定出口与 Lua 白名单")]
        public static void Run()
        {
            var catalog = new Dictionary<string, RoomDraftModel>(StringComparer.Ordinal);
            var world = WorldLayout.Read(Path.Combine(Application.streamingAssetsPath, "World/main.world.json"), catalog);
            var campaign = PuzzleRun.Campaign();
            Whitelist(catalog["warehouse"].Room.ObjectsOf(ObjectCategories.Box).First().Id);
            int passed = 0, total = 0;
            foreach (var placement in world.Rooms)
            {
                string id = placement.LevelId;
                var model = catalog[id];
                Require(!RoomValidator.HasErrors(RoomValidator.ValidateDraft(model, ValidationStage.Publish)), "发布级校验未通过 " + id);
                Debug.Log("FLOWQA STRUCTURE PASS " + id);
                var starts = Starts(id, model, world);
                var report = RoomSolution.CheckEntries(model, world).Split('\n');
                Require(report.Length == starts.Count, "CheckEntries 行数与起点数不符 " + id);
                foreach (var start in starts)
                {
                    total++;
                    try { Route(id, model, start.Key, start.Value, report); passed++; }
                    catch (Exception ex) { Debug.LogError($"FLOWQA FAIL {id} {start.Key} {ex}"); }
                }
            }
            Debug.Log($"FLOWQA RESULT routes={passed}/{total} rooms={catalog.Count}");
            Require(passed == total && total == 7 && catalog.Count == 3 && campaign.Rooms.Count == 3, "formal routes incomplete");
        }
    }
}
