/**
 * @file EditCommands.cs
 * @brief 房间草稿模型与放置类编辑命令：草稿持有 Room、门、主入口与恢复道具，命令先预检再提交，非法则抛 InvalidOperationException、不压栈。
 * RoomDraftModel：编辑中的房间，允许缺出生点、缺主入口、没有恢复道具等中间态；恢复道具 ID 与房内对象共用 Room 的 ID 空间；Extras 是 string 到 string 的透传字段包，存放旧房间里 Core 不解释的字段原文，默认为空。
 * PlaceObjectCommand(draft, id, categoryId, cell)：先经 PlacementRules.Error 预检（越界、空格、墙、门格或主入口预留格、已占用），再放置对象，ID 不得重复；Revert 移除并释放 ID。
 * MoveObjectCommand(draft, id, cell, syncRecoveryCell)：按 ID 移动对象，经 PlacementRules.Error 预检（对象可留在原格），syncRecoveryCell 为真时箱子的恢复格同步到新格；找不到对象或预检失败抛 InvalidOperationException；装饰类型与外观引用随对象保留；Revert 还原原格与原恢复格。
 * SetSpawnCommand(draft)：出生点固定在主入口内侧格（门格减外侧偏移），没有主入口、该格不是地板或被其他对象占用时抛 InvalidOperationException；重复设置幂等；Revert 还原旧出生点或移除。
 * AddRecoveryItemCommand(draft, item)：添加恢复道具，时长必须大于 0、ID 不得重复；Revert 移除并释放 ID。
 * SetRecoveryDurationCommand(draft, itemId, duration)：修改恢复道具时长，必须大于 0；Revert 还原旧时长。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class RoomDraftModel
    {
        readonly List<RecoveryItem> items = new List<RecoveryItem>();
        readonly List<DoorInfo> doors = new List<DoorInfo>();

        public Room Room { get; }
        public string PrimaryEntranceId { get; set; }
        public IReadOnlyList<RecoveryItem> RecoveryItems => items;
        public IReadOnlyList<DoorInfo> Doors => doors;
        public Dictionary<string, string> Extras { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public RoomDraftModel(Room room, IEnumerable<DoorInfo> doors = null, string primaryEntranceId = null, IEnumerable<KeyValuePair<string, string>> extras = null)
        {
            Room = room;
            if (doors != null) this.doors.AddRange(doors);
            PrimaryEntranceId = primaryEntranceId;
            if (extras != null)
                foreach (var pair in extras) Extras[pair.Key] = pair.Value;
        }

        internal List<RecoveryItem> ItemList => items;
    }

    public sealed class PlaceObjectCommand : ICommand
    {
        readonly RoomDraftModel draft;
        readonly string id, categoryId;
        readonly Cell cell;

        public PlaceObjectCommand(RoomDraftModel draft, string id, string categoryId, Cell cell)
        {
            this.draft = draft;
            this.id = id;
            this.categoryId = categoryId;
            this.cell = cell;
        }

        public void Apply()
        {
            var room = draft.Room;
            if (string.IsNullOrEmpty(id) || room.Ids.Contains(id)) throw new InvalidOperationException("ID 为空或已存在：" + id);
            string error = PlacementRules.Error(draft, categoryId, cell, null);
            if (error != null) throw new InvalidOperationException("格子 " + cell + " 不能放置：" + error);
            room.AddObject(id, categoryId, cell);
        }

        public void Revert() => draft.Room.RemoveObject(id);
    }

    public sealed class MoveObjectCommand : ICommand
    {
        readonly RoomDraftModel draft;
        readonly string id;
        readonly Cell cell;
        readonly bool syncRecoveryCell;
        PlacedObject before;

        public MoveObjectCommand(RoomDraftModel draft, string id, Cell cell, bool syncRecoveryCell)
        {
            this.draft = draft;
            this.id = id;
            this.cell = cell;
            this.syncRecoveryCell = syncRecoveryCell;
        }

        public void Apply()
        {
            var room = draft.Room;
            var old = room.Objects.FirstOrDefault(o => o.Id == id);
            if (old == null) throw new InvalidOperationException("找不到对象：" + id);
            string error = PlacementRules.Error(draft, old.CategoryId, cell, id);
            if (error != null) throw new InvalidOperationException("格子 " + cell + " 不能放置：" + error);
            before = old;
            var recovery = syncRecoveryCell && old.CategoryId == ObjectCategories.Box ? cell : old.RecoveryCell;
            room.RemoveObject(id);
            room.AddObject(id, old.CategoryId, cell, recovery, old.DecorationKind, old.AppearanceRef);
        }

        public void Revert()
        {
            draft.Room.RemoveObject(id);
            draft.Room.AddObject(before.Id, before.CategoryId, before.Cell, before.RecoveryCell, before.DecorationKind, before.AppearanceRef);
        }
    }

    public sealed class SetSpawnCommand : ICommand
    {
        const string SpawnId = "spawn";
        readonly RoomDraftModel draft;
        Cell? previous;

        public SetSpawnCommand(RoomDraftModel draft)
        {
            this.draft = draft;
        }

        public void Apply()
        {
            var room = draft.Room;
            var door = draft.Doors.FirstOrDefault(d => d.Id == draft.PrimaryEntranceId && d.Role == "entry");
            if (door == null) throw new InvalidOperationException("没有主入口，无法设置出生点");
            var cell = new Cell(door.Cell.X - door.Outward.X, door.Cell.Y - door.Outward.Y);
            if (!room.Inside(cell) || room.TerrainAt(cell).CategoryId != TerrainCategories.Floor)
                throw new InvalidOperationException("格子 " + cell + " 不是地板");
            var old = room.ObjectsOf(ObjectCategories.Spawn).FirstOrDefault();
            var occupant = room.ObjectAt(cell);
            if (occupant != null && occupant != old) throw new InvalidOperationException("格子 " + cell + " 已有对象");
            previous = old?.Cell;
            if (old != null) room.RemoveObject(old.Id);
            room.AddObject(SpawnId, ObjectCategories.Spawn, cell);
        }

        public void Revert()
        {
            draft.Room.RemoveObject(SpawnId);
            if (previous.HasValue) draft.Room.AddObject(SpawnId, ObjectCategories.Spawn, previous.Value);
        }
    }

    public sealed class AddRecoveryItemCommand : ICommand
    {
        readonly RoomDraftModel draft;
        readonly RecoveryItem item;

        public AddRecoveryItemCommand(RoomDraftModel draft, RecoveryItem item)
        {
            this.draft = draft;
            this.item = item;
        }

        public void Apply()
        {
            if (item.Duration <= 0) throw new InvalidOperationException("恢复时长必须大于 0");
            if (string.IsNullOrEmpty(item.Id) || draft.Room.Ids.Contains(item.Id))
                throw new InvalidOperationException("ID 为空或已存在：" + item.Id);
            draft.Room.Ids.Add(item.Id, "recovery");
            draft.ItemList.Add(item);
        }

        public void Revert()
        {
            draft.ItemList.Remove(item);
            draft.Room.Ids.Remove(item.Id);
        }
    }

    public sealed class SetRecoveryDurationCommand : ICommand
    {
        readonly RoomDraftModel draft;
        readonly string itemId;
        readonly float duration;
        RecoveryItem before;
        RecoveryItem after;

        public SetRecoveryDurationCommand(RoomDraftModel draft, string itemId, float duration)
        {
            this.draft = draft;
            this.itemId = itemId;
            this.duration = duration;
        }

        public void Apply()
        {
            if (duration <= 0) throw new InvalidOperationException("恢复时长必须大于 0");
            int index = draft.ItemList.FindIndex(i => i.Id == itemId);
            if (index < 0) throw new InvalidOperationException("没有恢复道具 " + itemId);
            before = draft.ItemList[index];
            after = new RecoveryItem(before.Id, before.ScriptRef, duration, before.Params);
            draft.ItemList[index] = after;
        }

        public void Revert()
        {
            int index = draft.ItemList.IndexOf(after);
            if (index >= 0) draft.ItemList[index] = before;
        }
    }
}
