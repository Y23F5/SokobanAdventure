/**
 * @file EditCommands.cs
 * @brief 放置类编辑命令：命令先预检再提交，非法则抛 InvalidOperationException、不压栈；草稿模型本身在 RoomDraftModel.cs。
 * PlaceObjectCommand(draft, id, categoryId, cell, appearance, rotation)：先经 PlacementRules.Error 预检（越界、空格、墙、门格或主入口预留格、已占用），再放置对象并可附外观引用和旋转，ID 不得重复；Revert 移除并释放 ID。
 * MoveObjectCommand(draft, id, cell)：按 ID 移动对象并保留其外观引用和旋转，经 PlacementRules.Error 预检（对象可留在原格）；找不到对象或预检失败抛 InvalidOperationException；Revert 还原原格、外观与旋转。
 * SetSpawnCommand(draft, rotation)：出生点固定在主入口内侧格（门格减外侧偏移），没有主入口、该格不是地板或被其他对象占用时抛 InvalidOperationException；省略旋转时保留已有朝向，新出生点默认 0；Revert 还原旧出生点或移除。
 * PlaceObjectCommand、SetSpawnCommand 的 DraftHolder 重载在每次执行与撤销时解析当前草稿，支持与替换草稿的笔划命令混用。
 * AddRecoveryItemCommand(draft, item)：添加恢复道具，时长必须大于 0、ID 不得重复；Revert 移除并释放 ID。
 * SetRecoveryDurationCommand(draft, itemId, duration)：修改恢复道具时长，必须大于 0；Revert 还原旧时长。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class PlaceObjectCommand : ICommand
    {
        readonly DraftHolder holder;
        readonly string id, categoryId;
        readonly Cell cell;
        readonly AppearanceRef appearance;
        readonly int rotation;

        public PlaceObjectCommand(RoomDraftModel draft, string id, string categoryId, Cell cell, AppearanceRef appearance = null, int rotation = 0)
            : this(new DraftHolder(draft), id, categoryId, cell, appearance, rotation) { }

        public PlaceObjectCommand(DraftHolder holder, string id, string categoryId, Cell cell, AppearanceRef appearance = null, int rotation = 0)
        {
            this.holder = holder ?? throw new ArgumentNullException(nameof(holder));
            this.id = id;
            this.categoryId = categoryId;
            this.cell = cell;
            this.appearance = appearance;
            this.rotation = RotationSteps.Validate(rotation);
        }

        public void Apply()
        {
            var draft = holder.Draft;
            var room = draft.Room;
            if (string.IsNullOrEmpty(id) || room.Ids.Contains(id)) throw new InvalidOperationException("ID 为空或已存在：" + id);
            string error = PlacementRules.Error(draft, categoryId, cell, null);
            if (error != null) throw new InvalidOperationException("格子 " + cell + " 不能放置：" + error);
            room.AddObject(id, categoryId, cell, appearance, rotation);
        }

        public void Revert() => holder.Draft.Room.RemoveObject(id);
    }

    public sealed class MoveObjectCommand : ICommand
    {
        readonly RoomDraftModel draft;
        readonly string id;
        readonly Cell cell;
        PlacedObject before;

        public MoveObjectCommand(RoomDraftModel draft, string id, Cell cell)
        {
            this.draft = draft;
            this.id = id;
            this.cell = cell;
        }

        public void Apply()
        {
            var room = draft.Room;
            var old = room.Objects.FirstOrDefault(o => o.Id == id);
            if (old == null) throw new InvalidOperationException("找不到对象：" + id);
            string error = PlacementRules.Error(draft, old.CategoryId, cell, id);
            if (error != null) throw new InvalidOperationException("格子 " + cell + " 不能放置：" + error);
            before = old;
            room.RemoveObject(id);
            room.AddObject(id, old.CategoryId, cell, old.Appearance, old.Rotation);
        }

        public void Revert()
        {
            draft.Room.RemoveObject(id);
            draft.Room.AddObject(before.Id, before.CategoryId, before.Cell, before.Appearance, before.Rotation);
        }
    }

    public sealed class SetSpawnCommand : ICommand
    {
        const string SpawnId = "spawn";
        readonly DraftHolder holder;
        readonly int? rotation;
        PlacedObject previous;

        public SetSpawnCommand(RoomDraftModel draft, int? rotation = null)
            : this(new DraftHolder(draft), rotation) { }

        public SetSpawnCommand(DraftHolder holder, int? rotation = null)
        {
            this.holder = holder ?? throw new ArgumentNullException(nameof(holder));
            if (rotation.HasValue) RotationSteps.Validate(rotation.Value);
            this.rotation = rotation;
        }

        public void Apply()
        {
            var draft = holder.Draft;
            var room = draft.Room;
            var door = draft.Doors.FirstOrDefault(d => d.Id == draft.PrimaryEntranceId && d.Role == "entry");
            if (door == null) throw new InvalidOperationException("没有主入口，无法设置出生点");
            var cell = new Cell(door.Cell.X - door.Outward.X, door.Cell.Y - door.Outward.Y);
            if (!room.Inside(cell) || room.TerrainAt(cell).CategoryId != TerrainCategories.Floor)
                throw new InvalidOperationException("格子 " + cell + " 不是地板");
            var old = room.ObjectsOf(ObjectCategories.Spawn).FirstOrDefault();
            var occupant = room.ObjectAt(cell);
            if (occupant != null && occupant != old) throw new InvalidOperationException("格子 " + cell + " 已有对象");
            previous = old;
            if (old != null) room.RemoveObject(old.Id);
            room.AddObject(SpawnId, ObjectCategories.Spawn, cell, old?.Appearance, rotation ?? old?.Rotation ?? 0);
        }

        public void Revert()
        {
            var room = holder.Draft.Room;
            room.RemoveObject(SpawnId);
            if (previous != null) room.AddObject(previous.Id, previous.CategoryId, previous.Cell, previous.Appearance, previous.Rotation);
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
