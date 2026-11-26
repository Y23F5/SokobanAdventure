/**
 * @file PuzzleTravel.cs
 * @brief 探索、道具背包、跨房与恢复的会话接口，规则全部委托 Core。
 * Enter()：首次入房记入探索集合并发放该房的恢复道具，探索集合与道具背包是两份数据。
 * CycleItem、AcceptItem、ItemRoomName：滚轮切换（恢复期间忽略）、确认全屏通知、按道具 ID 查所属房间的显示名。
 * Route、Allowed、CanApproach、CrossDoor：按门身份寻找正反方向连接，检查放行条件，全部通过后才提交跨房。
 * BeginRecovery()：选中项必须是本房恢复道具且未处于恢复中，时长取该道具的 Duration，由 RecoverySession.Start 计算占用区并移位玩家。
 * AdvanceRecovery(seconds)：由 RecoverySession.Advance 推进，完成时箱子一次性归位，玩家已按完成时刻的禁入集移位，返回本次是否完成。
 * RecoveryMoves、RecoveryGates：给表现层的恢复任务描述，需要归位的箱子与默认外观的可开合地形格。
 * ObjectStatesIn、SetObjectState：对象状态以房间隔离保存，由对象状态模块读写。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;

namespace Sokoban
{
    public readonly struct RecoveryMove
    {
        public readonly string BoxId;
        public readonly Cell From;
        public readonly Cell Home;
        public RecoveryMove(string boxId, Cell from, Cell home) { BoxId = boxId; From = from; Home = home; }
    }

    public readonly struct RecoveryGate
    {
        public readonly Cell Cell;
        public readonly float Yaw;
        public RecoveryGate(Cell cell, float yaw) { Cell = cell; Yaw = yaw; }
    }

    public sealed partial class PuzzleRun
    {
        public const string DefaultGateScript = "forest-vine";
        public IReadOnlyList<string> Inventory => bag.Items;
        public string SelectedItem => bag.Selected;
        public string PendingItem => bag.Pending;
        public bool Recovering => Current.Session.Recovering;
        public float RecoveryRemaining => Current.Session.Remaining;
        public float RecoveryDuration => Current.Session.Duration;
        public float RecoveryFraction => Recovering ? 1 - Current.Session.Remaining / Current.Session.Duration : 0;
        public RecoveryItem ActiveItem => Current.Validated.RecoveryItems.FirstOrDefault(i => i.Id == bag.Selected);
        public bool Visited(string roomId) => exploration.Contains(roomId);
        public void AcceptItem() => bag.Accept();
        public void CycleItem(int delta) { if (!Recovering) bag.Cycle(delta); }

        public string ItemRoomName(string itemId)
        {
            var room = states.Values.Select(s => s.Validated).FirstOrDefault(r => r.RecoveryItems.Any(i => i.Id == itemId));
            return room?.Meta.Name ?? itemId;
        }

        void Enter()
        {
            if (!exploration.Visit(ActiveId)) return;
            var item = Current.Item;
            if (item != null) bag.Grant(item.Id);
        }

        DoorLink Route(DoorInfo door)
        {
            if (World == null) return null;
            foreach (var link in World.Connections)
            {
                if (link.From.LevelId == ActiveId && link.From.DoorId == door.Id) return new DoorLink(link, false);
                if (link.To.LevelId == ActiveId && link.To.DoorId == door.Id) return new DoorLink(link, true);
            }
            return null;
        }

        sealed class DoorLink
        {
            internal readonly WorldConnection Link;
            internal readonly bool Reverse;
            internal DoorLink(WorldConnection link, bool reverse) { Link = link; Reverse = reverse; }
        }

        bool Allowed(DoorLink route)
        {
            if (route.Reverse) return route.Link.AllowReturn;
            var dest = World.Rooms.First(r => r.LevelId == route.Link.To.LevelId);
            return Solved && dest.ForwardRequiresSolved.All(id => states[id].Solved);
        }

        public bool CanApproach(DoorInfo door)
        {
            var route = Route(door);
            return route == null ? door.Role == "entry" || Solved : Allowed(route);
        }

        bool CrossDoor(Cell direction)
        {
            if (Recovering) return false;
            var door = Current.Validated.Doors.FirstOrDefault(d => d.Cell == local && d.Outward == direction);
            if (door == null) return false;
            var route = Route(door);
            if (route == null || !Allowed(route)) return false;
            var end = route.Reverse ? route.Link.From : route.Link.To;
            var dest = states[end.LevelId];
            var target = dest.Validated.Doors.First(d => d.Id == end.DoorId);
            if (dest.Boxes.ContainsValue(target.Cell)) return false;
            ActiveId = end.LevelId;
            local = target.Cell;
            Enter();
            return true;
        }

        public bool BeginRecovery()
        {
            var item = ActiveItem;
            if (item == null || Recovering || Motion != null) return false;
            var session = Current.Rebuild(local);
            session.Start(item.Duration);
            local = session.Player;
            return true;
        }

        public bool AdvanceRecovery(float seconds)
        {
            var session = Current.Session;
            if (!session.Recovering) return false;
            session.Place(local);
            if (!session.Advance(seconds)) return false;
            local = session.Player;
            Current.RecordSolved();
            Fire(ActiveId, TriggerIds.RecoveryEnd);
            return true;
        }

        void Fire(string roomId, string trigger, string only = null)
        {
            var progress = states[roomId];
            foreach (var pair in Models[roomId].ObjectStates)
            {
                if (only != null && pair.Key != only) continue;
                string from = progress.ObjectStates.TryGetValue(pair.Key, out var now) ? now : pair.Value.Initial;
                var move = pair.Value.Find(from, trigger);
                if (move == null) continue;
                SetObjectState(roomId, pair.Key, move.To == pair.Value.Initial ? null : move.To);
            }
        }

        public bool PlayerOnClosingCell
        {
            get
            {
                if (!Recovering) return false;
                var room = Current.Validated.Room;
                return room.TerrainAt(local).CategoryId == TerrainCategories.Openable || room.ObjectsOf(ObjectCategories.Box).Any(b => b.Cell == local);
            }
        }

        public IReadOnlyList<RecoveryMove> RecoveryMoves()
        {
            var list = new List<RecoveryMove>();
            foreach (var box in Current.Validated.Room.ObjectsOf(ObjectCategories.Box).OrderBy(b => b.Id, StringComparer.Ordinal))
                if (Current.Boxes.TryGetValue(box.Id, out var now) && now != box.Cell) list.Add(new RecoveryMove(box.Id, now, box.Cell));
            return list;
        }

        public IReadOnlyList<RecoveryGate> RecoveryGates()
        {
            var room = Current.Validated.Room;
            var list = new List<RecoveryGate>();
            bool Passage(Cell cell) { string id = room.TerrainAt(cell).CategoryId; return id == TerrainCategories.Floor || id == TerrainCategories.Door; }
            for (int x = 0; x < room.Width; x++)
                for (int y = 0; y < room.Height; y++)
                {
                    var cell = new Cell(x, y);
                    if (room.TerrainAt(cell).CategoryId != TerrainCategories.Openable || room.AppearanceAt(cell) != null) continue;
                    list.Add(new RecoveryGate(cell, Passage(cell + Cell.Left) && Passage(cell + Cell.Right) ? 90 : 0));
                }
            return list;
        }

        public IReadOnlyDictionary<string, string> ObjectStatesIn(string roomId) => states[roomId].ObjectStates;

        public void SetObjectState(string roomId, string objectId, string state)
        {
            var table = states[roomId].ObjectStates;
            if (state == null) table.Remove(objectId); else table[objectId] = state;
        }
    }
}
