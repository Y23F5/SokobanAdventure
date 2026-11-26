/**
 * @file RecoverySession.cs
 * @brief RecoverySession 是单房恢复过程的规则状态：开始时算占用区并移位玩家，期间禁推、可开合地形开放，完成时箱子一次性归位并封闭。
 * RecoverySession(room, boxes, player, entryBuffer)：用房间、各箱当前格、玩家格与入口缓冲格建立会话，箱子以 ID 为键。
 * Start(duration)：时长必须大于 0；计算占用区，玩家在占用区内先被安全移位，可开合地形开放，进入恢复中。
 * Place(cell)：把会话记录的玩家位置同步为调用方实际走到的格子，PushRules 只返回新位置，不改会话，恢复完成时的移位以此为准。
 * Advance(seconds)：推进剩余时间，归零时自动完成，返回本次是否完成。
 * Complete()：箱子一次性回到初始格，可开合地形封闭，玩家按完成时刻的禁入集（各箱初始格、封闭地形格）再移位；重复调用不重复提交。
 * CanPush()：恢复期间一律为 false。CanEnter(cell)：占用区内不可进入。
 * IsWalkable(cell)：地形通行性，可开合地形随恢复状态变化。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class RecoverySession
    {
        readonly Room room;
        readonly Registry<TerrainCategory> terrain = TerrainCategories.CreateDefault();
        readonly Cell? entryBuffer;
        readonly Dictionary<string, Cell> initial = new Dictionary<string, Cell>();
        bool openablesOpen;

        public Dictionary<string, Cell> Boxes { get; }
        public Cell Player { get; private set; }
        public bool Recovering { get; private set; }
        public float Remaining { get; private set; }
        public float Duration { get; private set; }
        public HashSet<Cell> Zone { get; private set; } = new HashSet<Cell>();
        public int CompletedCount { get; private set; }

        public RecoverySession(Room room, IDictionary<string, Cell> boxes, Cell player, Cell? entryBuffer)
        {
            this.room = room;
            this.entryBuffer = entryBuffer;
            Player = player;
            Boxes = new Dictionary<string, Cell>(boxes);
            foreach (var box in room.ObjectsOf(ObjectCategories.Box)) initial[box.Id] = box.Cell;
        }

        public void Place(Cell cell) => Player = cell;

        public bool CanPush() => !Recovering;

        public bool CanEnter(Cell cell) => !(Recovering && Zone.Contains(cell));

        public bool IsWalkable(Cell cell)
        {
            var t = room.TerrainAt(cell);
            var category = terrain.Get(t.CategoryId);
            if (category.HasStates)
                return category.IsWalkable(Recovering && openablesOpen ? TerrainCategories.Open : t.State ?? category.InitialState);
            return category.IsWalkable(null);
        }

        public void Start(float duration)
        {
            if (duration <= 0) throw new ArgumentException("恢复时长必须大于 0", nameof(duration));
            if (Recovering) return;
            Duration = duration;
            Remaining = duration;
            Recovering = true;
            openablesOpen = true;
            Zone = RecoveryRules.OccupiedZone(Boxes.Select(b => new BoxMove(b.Value, initial[b.Key])));
            if (Zone.Contains(Player))
                Player = RecoveryRules.SafeCell(Player, IsWalkable, Zone, new HashSet<Cell>(Boxes.Values), entryBuffer);
        }

        public bool Advance(float seconds)
        {
            if (!Recovering) return false;
            Remaining = Math.Max(0, Remaining - Math.Max(0, seconds));
            if (Remaining > 0) return false;
            Complete();
            return true;
        }

        public void Complete()
        {
            if (!Recovering) return;
            foreach (var id in Boxes.Keys.ToList()) Boxes[id] = initial[id];
            Recovering = false;
            openablesOpen = false;
            Zone = new HashSet<Cell>();
            CompletedCount++;
            var forbidden = new HashSet<Cell>(initial.Values);
            for (int x = 0; x < room.Width; x++)
                for (int y = 0; y < room.Height; y++)
                {
                    var c = new Cell(x, y);
                    if (!IsWalkable(c)) forbidden.Add(c);
                }
            if (forbidden.Contains(Player))
                Player = RecoveryRules.SafeCell(Player, c => c == Player || IsWalkable(c), forbidden,
                    new HashSet<Cell>(Boxes.Values), entryBuffer);
        }
    }
}
