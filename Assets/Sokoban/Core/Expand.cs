/**
 * @file Expand.cs
 * @brief 房间扩建的纯函数与命令，只在单独编辑房间时使用，世界不自动补偿。
 * Room.Resized(shiftX, shiftY, newWidth, newHeight)：返回平移后的新房间，地形、地形实体与对象格坐标整体平移，越界内容丢弃前须由调用方预检。
 * RoomResize.Resized(draft, shiftX, shiftY, newWidth, newHeight)：草稿重载，在房间平移之外让每扇门的格子同步平移，门地形与门 ID 对应的地形实体保持一致，
 * 并把恢复道具重新登记进新房间的 ID 空间，主入口与透传字段原样带到新草稿；返回全新的草稿，不改动原草稿；任一扇门平移后落到新房间之外时抛 InvalidOperationException。
 * ExpandCommand(holder, left, bottom, right, top)：带偏移量的复合命令，Apply 按四向扩建量生成新房间，Revert 按负偏移裁回旧尺寸并还原。
 * ExpandCommand.Precheck()：新尺寸必须为正，且扩建量不得为负，否则整条命令不提交。
 * RoomHolder：持有当前房间引用，使命令替换房间后外部仍能读到最新房间。
 */
using System;
using System.Collections.Generic;

namespace Sokoban.Core
{
    public sealed class RoomHolder
    {
        public Room Room;
        public RoomHolder(Room room) { Room = room; }
    }

    public static class RoomResize
    {
        public static Room Resized(Room source, int shiftX, int shiftY, int newWidth, int newHeight)
        {
            var next = new Room(source.Id, newWidth, newHeight);
            for (int x = 0; x < source.Width; x++)
                for (int y = 0; y < source.Height; y++)
                {
                    var t = source.TerrainAt(new Cell(x, y));
                    var to = new Cell(x + shiftX, y + shiftY);
                    if (!next.Inside(to)) continue;
                    next.SetTerrain(to, t.CategoryId, t.EntityId, t.State);
                }
            foreach (var o in source.Objects)
            {
                var to = new Cell(o.Cell.X + shiftX, o.Cell.Y + shiftY);
                if (!next.Inside(to)) continue;
                next.AddObject(o.Id, o.CategoryId, to);
            }
            return next;
        }

        public static RoomDraftModel Resized(RoomDraftModel source, int shiftX, int shiftY, int newWidth, int newHeight)
        {
            var room = Resized(source.Room, shiftX, shiftY, newWidth, newHeight);
            var doors = new List<DoorInfo>();
            foreach (var door in source.Doors)
            {
                var cell = new Cell(door.Cell.X + shiftX, door.Cell.Y + shiftY);
                if (!room.Inside(cell)) throw new InvalidOperationException("门 " + door.Id + " 平移后超出房间：" + cell);
                var terrain = room.TerrainAt(cell);
                if (terrain.CategoryId != TerrainCategories.Door || terrain.EntityId != door.Id)
                    room.SetTerrain(cell, TerrainCategories.Door, door.Id, terrain.State);
                doors.Add(new DoorInfo(door.Id, cell, door.Outward, door.Role));
            }
            var next = new RoomDraftModel(room, doors, source.PrimaryEntranceId, source.Extras);
            foreach (var item in source.RecoveryItems)
            {
                room.Ids.Add(item.Id, "recovery");
                next.ItemList.Add(item);
            }
            return next;
        }
    }

    public sealed class ExpandCommand : ICommand
    {
        readonly RoomHolder holder;
        readonly int left, bottom, right, top;
        Room before;

        public ExpandCommand(RoomHolder holder, int left, int bottom, int right, int top)
        {
            this.holder = holder;
            this.left = left;
            this.bottom = bottom;
            this.right = right;
            this.top = top;
        }

        public string Precheck()
        {
            if (left < 0 || bottom < 0 || right < 0 || top < 0) return "扩建量不能为负";
            if (left + bottom + right + top == 0) return "扩建量为 0";
            return null;
        }

        public void Apply()
        {
            var error = Precheck();
            if (error != null) throw new InvalidOperationException(error);
            before = holder.Room;
            var r = before;
            holder.Room = RoomResize.Resized(r, left, bottom, r.Width + left + right, r.Height + bottom + top);
        }

        public void Revert()
        {
            holder.Room = before;
        }
    }
}
