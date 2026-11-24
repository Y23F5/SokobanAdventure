/**
 * @file WS06FormalRoutesVerification.cs
 * @brief Run 从正式世界连接枚举准入端点。Route 在首次到房快照上恢复指定起点，逐步回放到出口。
 * Whitelist 检查未导出游戏动作及已导出动作对照。Require 将验收差异转成批处理失败。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using MoonSharp.Interpreter;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class WS06FormalRoutesVerification
    {
        static readonly Vector2Int[] Moves = {Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left};
        static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

        static void Whitelist(string owner)
        {
            var effects = new List<JToken>();
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
            Require(effects.Count == 1 && (string)effects[0]["kind"] == "setVisible" && (bool?)effects[0]["visible"] == false, "positive control failed");
            Debug.Log("FLOWQA ALLOWED self.setVisible(false) effects=1");
        }

        static void Route(PuzzleRun run, JObject arrival, string label, Vector2Int start, JToken exit, string[] report)
        {
            var state = (JObject)arrival.DeepClone();
            state["player"] = RoomFile.Point(run.Origin + start);
            run.Restore(state);
            string id = run.ActiveId;
            Require(!run.Solved && run.Room["boxes"].All(b => run.Boxes[(string)b["id"]] == run.Origin + RoomFile.At(b)), "initial box layout");
            var end = RoomFile.At(exit);
            var result = RoomSolution.Check(run.Room, start, 100000, run.World, end);
            Debug.Log($"FLOWQA SEARCH {id} {label} → {exit["id"]} {result.Status} expanded={result.Expanded} path={result.Detail}");
            Require(result.Status == RoomSolution.Outcome.Solved, result.Detail);
            Require(report.Count(line => line == $"{label} → {exit["id"]} · 有解 {result.Detail}") == 1, "CheckEntries mismatch");
            int solvedAt = 0;
            for (int i = 0; i < result.Detail.Length; i++)
            {
                int direction = "NESW".IndexOf(result.Detail[i]);
                Require(direction >= 0 && run.Step(Moves[direction], out _) && run.ActiveId == id, $"rejected or crossed at step {i+1}");
                if (solvedAt == 0 && run.Solved) solvedAt = i + 1;
            }
            Require(solvedAt > 0 && run.Solved && run.Targets.All(t => run.Boxes.ContainsValue(t)) && run.Player == run.Origin + end, "wrong final state");
            Debug.Log($"FLOWQA REPLAY {id} {label} → {exit["id"]} steps={result.Detail.Length} solvedAt={solvedAt} local={end} world={run.Player}");
        }

        [MenuItem("Sokoban/验证/WS-06 正式三房指定出口与 Lua 白名单")]
        public static void Run()
        {
            var campaign = PuzzleRun.Campaign();
            Whitelist((string)campaign.Room["boxes"][0]["id"]);
            var arrivals = new Dictionary<string,JObject> { [campaign.ActiveId] = campaign.Snapshot() };
            const string reference = "RRRURRRRRURRRRRRLDLDDLUULURRRDLDDRRRRUURRRURUULURDRDRRDRRURRDDLUURULRDDDRR";
            foreach (char step in reference.Take(reference.Length - 1))
            {
                Require(campaign.Step(Moves["URDL".IndexOf(step)], out _), "campaign approach rejected");
                if (!arrivals.ContainsKey(campaign.ActiveId)) arrivals.Add(campaign.ActiveId, campaign.Snapshot());
                if (arrivals.Count == campaign.Rooms.Count) break;
            }
            int passed = 0, total = 0;
            foreach (var placement in campaign.World["rooms"])
            {
                string id = (string)placement["levelId"];
                var arrival = arrivals[id];
                campaign.Restore(arrival);
                var room = campaign.Room;
                RoomFile.RequireStructure(room);
                Debug.Log("FLOWQA STRUCTURE PASS " + id);
                var starts = new Dictionary<string,Vector2Int> { ["出生点"] = RoomFile.At(room["spawn"]) };
                foreach (var link in campaign.World["connections"])
                {
                    var ends = new List<JToken> {link["forwardTo"]};
                    if ((bool?)link["allowReturn"] == true) ends.Add(link["forwardFrom"]);
                    foreach (var endpoint in ends.Where(e => (string)e["levelId"] == id))
                    {
                        string door = (string)endpoint["doorId"];
                        starts["门 " + door] = RoomFile.At(room["doors"].Single(d => (string)d["id"] == door));
                    }
                }
                var report = RoomSolution.CheckEntries(room, campaign.World).Split('\n');
                foreach (var exit in room["doors"].Where(d => (string)d["role"] == "exit"))
                    foreach (var start in starts)
                    {
                        total++;
                        try { Route(campaign, arrival, start.Key, start.Value, exit, report); passed++; }
                        catch (Exception ex) { Debug.LogError($"FLOWQA FAIL {id} {start.Key} {ex}"); }
                    }
            }
            Debug.Log($"FLOWQA RESULT routes={passed}/{total} rooms={arrivals.Count}");
            Require(passed == total && total == 7 && arrivals.Count == 3, "formal routes incomplete");
        }
    }
}
