/**
 * @file Solver.cs
 * @brief 单房求解器：对玩家位置加箱位做广度优先搜索，只模拟走路与推箱，不建模恢复，可开合地形按封闭处理。
 * SolveOutcome：确认有解、确认无解、搜索未完成三种结果；含不可模拟节点的房间由上层报无法验证。
 * SolveResult：结果、解法方向序列（Cell 方向）与已展开状态数。
 * Solver.Solve(room, start, budget)：start 为出生点，budget 为最多展开的状态数；找到解后用真实推动规则回放确认，回放失败视为搜索未完成。
 * Solver.Replay(room, start, moves)：用 PushRules 与 RecoverySession 的真实规则逐步回放，返回结束时是否解开。
 * 状态以玩家格加排序后的箱位表示；箱子视为同类，不区分 ID。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public enum SolveOutcome { Solved, Unsolvable, BudgetReached }

    public sealed class SolveResult
    {
        public SolveOutcome Outcome { get; }
        public IReadOnlyList<Cell> Moves { get; }
        public int Expanded { get; }

        public SolveResult(SolveOutcome outcome, IReadOnlyList<Cell> moves, int expanded)
        {
            Outcome = outcome;
            Moves = moves;
            Expanded = expanded;
        }
    }

    public static class Solver
    {
        public static SolveResult Solve(Room room, Cell start, int budget)
        {
            var terrain = TerrainCategories.CreateDefault();
            bool Walk(Cell c)
            {
                var t = room.TerrainAt(c);
                var cat = terrain.Get(t.CategoryId);
                return cat.HasStates ? cat.IsWalkable(t.State ?? cat.InitialState) : cat.IsWalkable(null);
            }

            var boxes0 = room.ObjectsOf(ObjectCategories.Box).Select(b => b.Cell).OrderBy(c => c.X).ThenBy(c => c.Y).ToArray();
            var targets = room.ObjectsOf(ObjectCategories.Target).Select(t => t.Cell).ToArray();
            if (targets.Length == 0) return new SolveResult(SolveOutcome.Unsolvable, new Cell[0], 0);

            string Key(Cell p, Cell[] b) => p + "|" + string.Join(";", b.Select(c => c.ToString()));
            bool Goal(Cell[] b) => targets.All(t => Array.IndexOf(b, t) >= 0);

            var parent = new Dictionary<string, (string prev, Cell move)>();
            var states = new Dictionary<string, (Cell p, Cell[] b)>();
            var queue = new Queue<string>();
            string startKey = Key(start, boxes0);
            states[startKey] = (start, boxes0);
            parent[startKey] = (null, default);
            queue.Enqueue(startKey);
            int expanded = 0;

            while (queue.Count > 0)
            {
                if (expanded >= budget) return new SolveResult(SolveOutcome.BudgetReached, new Cell[0], expanded);
                var key = queue.Dequeue();
                expanded++;
                var (p, b) = states[key];
                if (Goal(b))
                {
                    var moves = new List<Cell>();
                    for (var k = key; parent[k].prev != null; k = parent[k].prev) moves.Add(parent[k].move);
                    moves.Reverse();
                    if (Replay(room, start, moves)) return new SolveResult(SolveOutcome.Solved, moves, expanded);
                    return new SolveResult(SolveOutcome.BudgetReached, new Cell[0], expanded);
                }
                foreach (var d in Cell.SearchOrder)
                {
                    var next = p + d;
                    if (!Walk(next)) continue;
                    var nb = b;
                    int bi = Array.IndexOf(b, next);
                    if (bi >= 0)
                    {
                        var beyond = next + d;
                        if (!Walk(beyond) || Array.IndexOf(b, beyond) >= 0) continue;
                        nb = (Cell[])b.Clone();
                        nb[bi] = beyond;
                        Array.Sort(nb, (a, c) => a.X != c.X ? a.X.CompareTo(c.X) : a.Y.CompareTo(c.Y));
                    }
                    string nk = Key(next, nb);
                    if (parent.ContainsKey(nk)) continue;
                    parent[nk] = (key, d);
                    states[nk] = (next, nb);
                    queue.Enqueue(nk);
                }
            }
            return new SolveResult(SolveOutcome.Unsolvable, new Cell[0], expanded);
        }

        public static bool Replay(Room room, Cell start, IEnumerable<Cell> moves)
        {
            var boxes = room.ObjectsOf(ObjectCategories.Box).ToDictionary(b => b.Id, b => b.Cell);
            var session = new RecoverySession(room, boxes, start, null);
            var player = start;
            foreach (var d in moves)
            {
                var r = PushRules.TryPush(session, player, d);
                if (!r.Moved) return false;
                player = r.Player;
            }
            return PushRules.Solved(room, session.Boxes);
        }
    }
}
