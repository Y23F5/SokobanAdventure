/**
 * @file EditCommands.cs
 * @brief 房间草稿模型与放置类编辑命令：草稿持有 Room、门、主入口与恢复道具，命令先预检再提交，非法则抛 InvalidOperationException、不压栈。
 * RoomDraft：编辑中的房间，允许缺出生点、缺主入口、没有恢复道具等中间态；恢复道具 ID 与房内对象共用 Room 的 ID 空间。
 * PlaceObjectCommand(draft, id, categoryId, cell)：在地板空格放置一个对象，ID 不得重复；Revert 移除并释放 ID。
 * SetSpawnCommand(draft, cell)：把唯一的出生点移到指定地板空格，没有则新建；Revert 还原旧出生点或移除。
 * AddRecoveryItemCommand(draft, item)：添加恢复道具，时长必须大于 0、ID 不得重复；Revert 移除并释放 ID。
 * SetRecoveryDurationCommand(draft, itemId, duration)：修改恢复道具时长，必须大于 0；Revert 还原旧时长。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class RoomDraft
    {
        readonly List<RecoveryItem> items = new List<RecoveryItem>();
        readonly List<DoorInfo> doors = new List<DoorInfo>();

        public Room Room { get; }
        public string PrimaryEntranceId { get; set; }
        public IReadOnlyList<RecoveryItem> RecoveryItems => items;
        public IReadOnlyList<DoorInfo> Doors => doors;

        public RoomDraft(Room room, IEnumerable<DoorInfo> doors = null, string primaryEntranceId = null)
        {
            Room = room;
            if (doors != null) this.doors.AddRange(doors);
            PrimaryEntranceId = primaryEntranceId;
        }

        internal List<RecoveryItem> ItemList => items;
    }

    public sealed class PlaceObjectCommand : ICommand
    {
        readonly RoomDraft draft;
        readonly string id, categoryId;
        readonly Cell cell;

        public PlaceObjectCommand(RoomDraft draft, string id, string categoryId, Cell cell)
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
            if (!room.Inside(cell) || room.TerrainAt(cell).CategoryId != TerrainCategories.Floor)
                throw new InvalidOperationException("格子 " + cell + " 不是地板");
            if (room.ObjectAt(cell) != null) throw new InvalidOperationException("格子 " + cell + " 已有对象");
            room.AddObject(id, categoryId, cell);
        }

        public void Revert() => draft.Room.RemoveObject(id);
    }

    public sealed class SetSpawnCommand : ICommand
    {
        const string SpawnId = "spawn";
        readonly RoomDraft draft;
        readonly Cell cell;
        Cell? previous;

        public SetSpawnCommand(RoomDraft draft, Cell cell)
        {
            this.draft = draft;
            this.cell = cell;
        }

        public void Apply()
        {
            var room = draft.Room;
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
        readonly RoomDraft draft;
        readonly RecoveryItem item;

        public AddRecoveryItemCommand(RoomDraft draft, RecoveryItem item)
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
        readonly RoomDraft draft;
        readonly string itemId;
        readonly float duration;
        RecoveryItem before;
        RecoveryItem after;

        public SetRecoveryDurationCommand(RoomDraft draft, string itemId, float duration)
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
