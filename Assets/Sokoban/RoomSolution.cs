/**
 * @file RoomSolution.cs
 * @brief Check 启动指定起点的单房搜索。Search 按四向步进遍历玩家和箱子的位置，返回可回放的方向序列。
 * Key 消除同类箱子排列的差别；Trace 从父状态还原走法。非静态事件与结构问题返回定位说明。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public static class RoomSolution
    {
        public enum Outcome { Solved, Unsolvable, BudgetReached, Unsupported, Invalid }
        public sealed class Result
        {
            public readonly Outcome Status;
            public readonly string Detail;
            public readonly int Expanded;
            internal Result(Outcome status, string detail, int expanded = 0)
            { Status = status; Detail = detail; Expanded = expanded; }
        }
        sealed class Node
        {
            internal Vector2Int Player;
            internal Vector2Int[] Boxes;
            internal Node Parent;
            internal char Direction;
        }
        static readonly Vector2Int[] Steps = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        const string Directions = "NESW";
        public static Result Check(JObject room, Vector2Int start, int budget = 100000, JObject world = null)
        {
            string issue = RoomFile.StructureIssues(room).FirstOrDefault();
            if (issue != null) return new Result(Outcome.Invalid, issue);
            var boxes = room["boxes"].Select(RoomFile.At).ToArray();
            if (!RoomFile.Floor(room, start) || boxes.Contains(start)) return new Result(Outcome.Invalid, "起点无法站立 " + start);
            var graph = room["events"].FirstOrDefault(item => !ObjectBehavior.IsVisual(item));
            if (graph != null)
            {
                var node = graph["nodes"]?.FirstOrDefault(item => !ObjectBehavior.IsVisualNode(item));
                return new Result(Outcome.Unsupported, $"待模拟对象 {graph["ownerId"]} 的行为图 {graph["id"]}，节点 {node?["id"]}（{node?["kind"]}）");
            }
            var targets = new HashSet<Vector2Int>(room["targets"].Select(RoomFile.At));
            if (targets.Count == 0 || boxes.Length < targets.Count) return new Result(Outcome.Unsolvable, "可占据的目标数量不足");
            var exits = new HashSet<Vector2Int>(room["doors"].Where(d =>
            {
                if ((string)d["role"] != "exit") return false;
                return world == null || !world["connections"].Any(link => (bool?)link["allowReturn"] == true &&
                    (string)link["forwardTo"]?["levelId"] == (string)room["levelId"] && (string)link["forwardTo"]?["doorId"] == (string)d["id"]);
            }).Select(RoomFile.At));
            if (targets.Any(goal => UnreachableTarget(room, boxes, goal))) return new Result(Outcome.Unsolvable, "存在箱子无法抵达的目标格");
            return Search(room, start, boxes, targets, exits, budget);
        }
        /** @brief 从目标反向枚举推箱前的箱格和玩家站位，忽略其他箱子。无箱子能抵达某个目标时可证明静态死锁。 */
        static bool UnreachableTarget(JObject room, Vector2Int[] boxes, Vector2Int target)
        {
            var queue = new Queue<Vector2Int>();
            var reachable = new HashSet<Vector2Int> { target };
            queue.Enqueue(target);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                if (Array.IndexOf(boxes, cell) >= 0) return false;
                foreach (var step in Steps)
                {
                    var before = cell - step;
                    if (RoomFile.Floor(room, before) && RoomFile.Floor(room, before - step) && reachable.Add(before)) queue.Enqueue(before);
                }
            }
            return true;
        }
        public static string CheckEntries(JObject room, JObject world, int budget = 100000)
        {
            string roomId = (string)room["levelId"];
            var entries = new Dictionary<string,Vector2Int> { ["出生点"] = RoomFile.At(room["spawn"]) };
            foreach (var door in room["doors"])
            {
                string id = (string)door["id"];
                bool Matches(JToken end) => (string)end?["levelId"] == roomId && (string)end?["doorId"] == id;
                bool accessible = world == null ? (string)door["role"] == "entry" : world["connections"].Any(link =>
                    Matches(link["forwardTo"]) || (bool?)link["allowReturn"] == true && Matches(link["forwardFrom"]));
                if (accessible) entries["门 " + id] = RoomFile.At(door);
            }
            var reports = new List<string>();
            foreach (var entry in entries)
            {
                var result = Check(room, entry.Value, budget, world);
                string status = result.Status == Outcome.Solved ? "有解" : result.Status == Outcome.Unsolvable ? "确认无解" :
                    result.Status == Outcome.BudgetReached ? "搜索未完成" : "无法验证";
                reports.Add(entry.Key + " · " + status + " " + result.Detail);
            }
            return string.Join("\n", reports);
        }
        static Result Search(JObject room, Vector2Int start, Vector2Int[] boxes, HashSet<Vector2Int> targets, HashSet<Vector2Int> exits, int budget)
        {
            var root = new Node { Player = start, Boxes = boxes };
            var queue = new Queue<Node>();
            var seen = new HashSet<string> { Key(root) };
            queue.Enqueue(root);
            int expanded = 0;
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (targets.All(target => Array.IndexOf(node.Boxes, target) >= 0)) return new Result(Outcome.Solved, Trace(node), expanded);
                if (expanded >= budget) return new Result(Outcome.BudgetReached, "搜索预算已用尽", expanded);
                expanded++;
                for (int i = 0; i < Steps.Length; i++)
                {
                    var next = node.Player + Steps[i];
                    if (!RoomFile.Floor(room, next) || exits.Contains(next)) continue;
                    var nextBoxes = node.Boxes;
                    int pushed = Array.IndexOf(nextBoxes, next);
                    if (pushed >= 0)
                    {
                        var beyond = next + Steps[i];
                        if (!RoomFile.Floor(room, beyond) || Array.IndexOf(nextBoxes, beyond) >= 0) continue;
                        nextBoxes = (Vector2Int[])nextBoxes.Clone();
                        nextBoxes[pushed] = beyond;
                    }
                    var child = new Node { Player = next, Boxes = nextBoxes, Parent = node, Direction = Directions[i] };
                    if (seen.Add(Key(child))) queue.Enqueue(child);
                }
            }
            return new Result(Outcome.Unsolvable, "已遍历此起点的全部可达状态", expanded);
        }
        static string Key(Node node) => node.Player.x + "," + node.Player.y + ":" + string.Join(";", node.Boxes.OrderBy(b => b.x).ThenBy(b => b.y).Select(b => b.x + "," + b.y));
        static string Trace(Node node)
        {
            var path = new Stack<char>();
            for (; node.Parent != null; node = node.Parent) path.Push(node.Direction);
            return new string(path.ToArray());
        }
    }
}
