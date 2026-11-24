/**
 * @file WorldSolution.cs
 * @brief Check searches room-to-room routes and replays each candidate with campaign movement. Describe formats the route or verification limit for both editors.
 */
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Result = Sokoban.RoomSolution.Result;
using Outcome = Sokoban.RoomSolution.Outcome;

namespace Sokoban
{
    public static class WorldSolution
    {
        static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        const string Letters = "URDL";

        public static Result Check(JObject world, JObject rooms, int budget = 100000)
        {
            var issue = WorldLayout.Issues(world, rooms).FirstOrDefault();
            if (issue != null) return new Result(Outcome.Invalid, issue);
            foreach (var placement in world["rooms"])
            {
                var room = rooms[(string)placement["levelId"]];
                var graph = room["events"].FirstOrDefault(g => !ObjectBehavior.CanSimulate(g));
                if (graph == null) continue;
                var node = graph["nodes"]?.FirstOrDefault(n => !ObjectBehavior.CanSimulateNode(n));
                return new Result(Outcome.Unsupported, $"{room["levelId"]}/{graph["ownerId"]}/{graph["id"]}/{node?["id"]}");
            }
            var run = PuzzleRun.FromWorld(world, rooms);
            var queue = new Queue<(JObject snapshot, string path)>();
            var seen = new HashSet<string>();
            queue.Enqueue((run.Snapshot(), ""));
            int expanded = 0;
            while (queue.Count > 0)
            {
                var branch = queue.Dequeue();
                run.Restore(branch.snapshot);
                var key = new JArray(run.ActiveId, RoomFile.Point(run.Player), branch.snapshot["rooms"]).ToString(Newtonsoft.Json.Formatting.None);
                if (!seen.Add(key)) continue;
                foreach (var door in run.Room["doors"].ToArray())
                {
                    run.Restore(branch.snapshot);
                    if (expanded >= budget) return new Result(Outcome.BudgetReached, "世界搜索预算已用尽", expanded);
                    var endpoint = world["finish"]["endpoint"];
                    bool Matches(JToken end) => (string)end["levelId"] == run.ActiveId && (string)end["doorId"] == (string)door["id"];
                    if (!Matches(endpoint) && !world["connections"].Any(link => Matches(link["forwardFrom"]) ||
                        (bool)link["allowReturn"] && Matches(link["forwardTo"]))) continue;
                    var result = RoomSolution.Check(run.Room, run.Player - run.Origin, budget - expanded, world,
                        RoomFile.At(door), run.Boxes.Values.Select(p => p - run.Origin).ToArray(), run.EverSolved);
                    expanded += System.Math.Max(1, result.Expanded);
                    if (result.Status != Outcome.Solved) continue;
                    string segment = new string(result.Detail.Select(c => Letters["NESW".IndexOf(c)]).ToArray());
                    if (!segment.All(c => run.Step(Directions[Letters.IndexOf(c)], out _))) continue;
                    var outward = -RoomFile.Inward(door);
                    var path = branch.path + segment + Letters[System.Array.IndexOf(Directions, outward)];
                    if (run.CanLeaveWorld(outward)) return new Result(Outcome.Solved, path, expanded);
                    if (run.Step(outward, out _)) { run.AcceptItem(); queue.Enqueue((run.Snapshot(), path)); }
                }
            }
            var oneWay = world["connections"].Where(link => (bool?)link["allowReturn"] == false)
                .Select(link => $"{link["forwardTo"]["levelId"]}/{link["forwardTo"]["doorId"]} → {link["forwardFrom"]["levelId"]}/{link["forwardFrom"]["doorId"]}");
            return new Result(Outcome.BudgetReached, "候选路线未到终点。禁止回退的方向：" + string.Join("，", oneWay), expanded);
        }

        public static string Describe(Result result)
        {
            string label = result.Status == Outcome.Solved ? "世界有解" : result.Status == Outcome.BudgetReached ? "世界搜索未完成" : "世界无法验证";
            return label + " · " + result.Detail;
        }
    }
}
