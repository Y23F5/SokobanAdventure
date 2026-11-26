/**
 * @file WorldSolution.cs
 * @brief 世界级求解：沿连接从起点房间逐房调用单房结论，前置房间解开后才放行后续房间，到达终点即给出整体结论。
 * Check(world, models, budget)：每个房间从进入它的门（起点房间从出生点）起算，求解器不建模跨房时的中途箱位，所以只比较各房初始布局的结论；
 * 任一经过的房间确认无解则整体确认无解，整体结论取路线上各房最保守的一档，终点不可达或含无法验证的房间时报无法验证。
 * Describe(result)：世界结论的中文文字。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using Result = Sokoban.RoomSolution.Result;

namespace Sokoban
{
    public static class WorldSolution
    {
        public static Result Check(WorldDocument world, IReadOnlyDictionary<string, RoomDraftModel> models, int budget = 100000)
        {
            foreach (var entry in world.Rooms)
                if (!models.ContainsKey(entry.LevelId)) return new Result(SolveVerdict.Unverifiable, "缺少房间 " + entry.LevelId, 0);
            var startModel = models[world.Start.LevelId];
            var spawn = startModel.Room.ObjectsOf(ObjectCategories.Spawn).FirstOrDefault(o => o.Id == world.Start.DoorId)
                ?? startModel.Room.ObjectsOf(ObjectCategories.Spawn).FirstOrDefault();
            if (spawn == null) return new Result(SolveVerdict.Unverifiable, "起点房间没有出生点", 0);
            var verdicts = new Dictionary<string, SolveVerdict>();
            var route = new List<string>();
            var entered = new HashSet<string> { world.Start.LevelId };
            var queue = new Queue<(string room, Cell entry)>();
            var waiting = new List<(string room, Cell entry)>();
            queue.Enqueue((world.Start.LevelId, spawn.Cell));
            int expanded = 0;
            while (queue.Count > 0)
            {
                var (id, entry) = queue.Dequeue();
                var result = RoomSolution.Check(models[id], entry, Math.Max(0, budget - expanded));
                expanded += result.Expanded;
                if (result.Verdict == SolveVerdict.Unsolvable) return new Result(SolveVerdict.Unsolvable, "房间 " + id + " 确认无解", expanded);
                if (result.Verdict == SolveVerdict.Unverifiable) continue;
                verdicts[id] = result.Verdict;
                route.Add(id);
                foreach (var door in models[id].Doors)
                    foreach (var link in world.Connections.Where(c => c.From.LevelId == id && c.From.DoorId == door.Id))
                    {
                        var destDoor = models[link.To.LevelId].Doors.FirstOrDefault(d => d.Id == link.To.DoorId);
                        if (destDoor != null && entered.Add(link.To.LevelId)) waiting.Add((link.To.LevelId, destDoor.Cell));
                    }
                foreach (var item in waiting.ToList())
                {
                    var need = world.Rooms.First(r => r.LevelId == item.room).ForwardRequiresSolved;
                    if (!need.All(verdicts.ContainsKey)) continue;
                    waiting.Remove(item);
                    queue.Enqueue(item);
                }
            }
            bool reached = verdicts.ContainsKey(world.FinishEndpoint.LevelId) && world.FinishRequiresSolved.All(verdicts.ContainsKey);
            if (!reached) return new Result(SolveVerdict.Unverifiable, "候选路线未到终点，已通过：" + string.Join("，", route), expanded);
            var worst = verdicts.Values.Any(v => v == SolveVerdict.Optimistic) ? SolveVerdict.Optimistic : SolveVerdict.Confirmed;
            return new Result(worst, string.Join(" → ", route), expanded);
        }

        public static string Describe(Result result) => "世界" + RoomSolution.Label(result.Verdict) + " · " + result.Detail;
    }
}
