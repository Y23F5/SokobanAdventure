/**
 * @file RoomSolution.cs
 * @brief 单房求解的对外入口，全部委托 Core 的 Solver.Judge，对外暴露确认有解、乐观有解、确认无解、无法验证四种结论。
 * Check(room, start, budget, hasUnsimulatable)：start 为起点格，返回结论、方向字母序列（U、R、D、L）与已展开状态数；含不可模拟节点由调用方传入。
 * Check(model, ...)：对草稿模型取其房间后同上。
 * CheckEntries(model, world, budget, hasUnsimulatable)：对出生点与当前可进入的门各出一行结论，单房取 entry 门，世界里取被连接指向或允许回退的门。
 * Label(verdict)：结论的中文名。Letters(moves)：把方向格序列写成字母。
 */
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;

namespace Sokoban
{
    public static class RoomSolution
    {
        public sealed class Result
        {
            public readonly SolveVerdict Verdict;
            public readonly string Detail;
            public readonly int Expanded;
            public readonly IReadOnlyList<Cell> Moves;
            internal Result(SolveVerdict verdict, string detail, int expanded, IReadOnlyList<Cell> moves = null)
            { Verdict = verdict; Detail = detail; Expanded = expanded; Moves = moves ?? new Cell[0]; }
        }

        public static Result Check(Room room, Cell start, int budget = 100000, bool hasUnsimulatable = false)
        {
            var judged = Solver.Judge(room, start, budget, hasUnsimulatable);
            return new Result(judged.Verdict, Letters(judged.Moves), judged.Expanded, judged.Moves);
        }

        public static Result Check(RoomDraftModel model, Cell start, int budget = 100000, bool hasUnsimulatable = false) =>
            Check(model.Room, start, budget, hasUnsimulatable);

        public static string CheckEntries(RoomDraftModel model, WorldDocument world, int budget = 100000, bool hasUnsimulatable = false)
        {
            var entries = new Dictionary<string, Cell>();
            var spawn = model.Room.ObjectsOf(ObjectCategories.Spawn).FirstOrDefault();
            if (spawn != null) entries["出生点"] = spawn.Cell;
            foreach (var door in model.Doors)
            {
                bool Matches(WorldEndpoint end) => end != null && end.LevelId == model.Room.Id && end.DoorId == door.Id;
                bool accessible = world == null ? door.Role == "entry" : world.Connections.Any(c => Matches(c.To) || (c.AllowReturn && Matches(c.From)));
                if (accessible) entries["门 " + door.Id] = door.Cell;
            }
            if (entries.Count == 0) return "没有可检查的起点：缺少出生点与入口门";
            return string.Join("\n", entries.Select(entry =>
            {
                var result = Check(model.Room, entry.Value, budget, hasUnsimulatable);
                return entry.Key + " · " + Label(result.Verdict) + " " + result.Detail;
            }));
        }

        public static string Label(SolveVerdict verdict) => verdict switch
        {
            SolveVerdict.Confirmed => "确认有解",
            SolveVerdict.Optimistic => "乐观有解",
            SolveVerdict.Unsolvable => "确认无解",
            _ => "无法验证"
        };

        public static string Letters(IEnumerable<Cell> moves) => new string(moves.Select(m =>
            m == Cell.Up ? 'U' : m == Cell.Right ? 'R' : m == Cell.Down ? 'D' : 'L').ToArray());
    }
}
