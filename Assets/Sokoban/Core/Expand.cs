/**
 * @file Expand.cs
 * @brief 房间扩建的纯函数与命令，只在单独编辑房间时使用，世界不自动补偿。
 * Room.Resized(shiftX, shiftY, newWidth, newHeight)：返回平移后的新房间，地形、地形实体、每格外观、对象格坐标与对象外观整体平移，越界内容丢弃前须由调用方预检。
 * RoomResize.Resized(draft, shiftX, shiftY, newWidth, newHeight)：草稿重载，在房间平移之外让每扇门、装饰与行为图节点的格坐标同步平移，门地形与门 ID 对应的地形实体保持一致，
 * 恢复道具参数中的格引用 gateX、gateZ 按偏移平移（无需平移时沿用原对象），资源、模板、实例、对象状态、界面图、显示名与透传字段原样带到新草稿，各 ID 重新登记进新房间的 ID 空间；
 * 返回全新的草稿，不改动原草稿；任一扇门平移后落到新房间之外时抛 InvalidOperationException。
 * ExpandCommand(holder, left, bottom, right, top)：只改房间的复合命令，Apply 按四向扩建量生成新房间，Revert 换回旧房间。
 * ExpandDraftCommand(holder, left, bottom, right, top, maxSide)：作用于整份草稿的扩建命令，Apply 记录旧尺寸并按偏移平移，Revert 按负偏移平移并裁回旧尺寸，严格后进先出。
 * Precheck()：新尺寸必须为正、不超过上限，且扩建量不得为负，否则整条命令不提交、不产生撤销记录。
 * RoomHolder：持有当前房间引用，使命令替换房间后外部仍能读到最新房间。
 */
using System;
using System.Collections.Generic;
using System.Linq;

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
                    var from = new Cell(x, y);
                    var t = source.TerrainAt(from);
                    var to = new Cell(x + shiftX, y + shiftY);
                    if (!next.Inside(to)) continue;
                    next.SetTerrain(to, t.CategoryId, t.EntityId, t.State);
                    var look = source.AppearanceAt(from);
                    if (look != null) next.SetAppearance(to, look);
                }
            foreach (var o in source.Objects)
            {
                var to = new Cell(o.Cell.X + shiftX, o.Cell.Y + shiftY);
                if (!next.Inside(to)) continue;
                next.AddObject(o.Id, o.CategoryId, to, o.Appearance);
            }
            return next;
        }

        static RecoveryItem Shifted(RecoveryItem item, int shiftX, int shiftY)
        {
            if ((shiftX == 0 && shiftY == 0) || !(item.Params.ContainsKey("gateX") || item.Params.ContainsKey("gateZ"))) return item;
            var parameters = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var p in item.Params)
                parameters[p.Key] = p.Key == "gateX" ? p.Value + shiftX : p.Key == "gateZ" ? p.Value + shiftY : p.Value;
            return new RecoveryItem(item.Id, item.ScriptRef, item.Duration, parameters);
        }

        static BehaviorGraph Shifted(BehaviorGraph graph, int shiftX, int shiftY)
        {
            var nodes = graph.Nodes.Select(n => n.Cell.HasValue
                ? new BehaviorNode(n.Id, n.Kind, new Cell(n.Cell.Value.X + shiftX, n.Cell.Value.Y + shiftY), n.Parameters)
                : n).ToList();
            return new BehaviorGraph(graph.Id, graph.OwnerId, nodes, graph.Edges);
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
            next.Meta = source.Meta;
            next.UiImageAssetId = source.UiImageAssetId;
            foreach (var item in source.RecoveryItems)
            {
                room.Ids.Add(item.Id, "recovery");
                next.ItemList.Add(Shifted(item, shiftX, shiftY));
            }
            foreach (var d in source.Decorations)
            {
                next.Register(d.Id, "decoration");
                next.Decorations.Add(new Decoration(d.Id, new Cell(d.Cell.X + shiftX, d.Cell.Y + shiftY), d.Appearance));
            }
            next.Assets.AddRange(source.Assets);
            foreach (var g in source.Graphs)
            {
                next.Register(g.Id, "graph");
                next.Graphs.Add(Shifted(g, shiftX, shiftY));
            }
            foreach (var t in source.Templates) next.Templates[t.Key] = t.Value;
            foreach (var i in source.Instances)
            {
                next.Register(i.Id, "instance");
                next.Instances.Add(i);
            }
            foreach (var s in source.ObjectStates) next.ObjectStates[s.Key] = s.Value;
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

    public sealed class ExpandDraftCommand : ICommand
    {
        readonly DraftHolder holder;
        readonly int left, bottom, right, top, maxSide;
        int oldWidth, oldHeight;

        public ExpandDraftCommand(DraftHolder holder, int left, int bottom, int right, int top, int maxSide = int.MaxValue)
        {
            this.holder = holder;
            this.left = left;
            this.bottom = bottom;
            this.right = right;
            this.top = top;
            this.maxSide = maxSide;
        }

        public string Precheck()
        {
            if (left < 0 || bottom < 0 || right < 0 || top < 0) return "扩建量不能为负";
            if (left + bottom + right + top == 0) return "扩建量为 0";
            var room = holder.Draft.Room;
            int width = room.Width + left + right, height = room.Height + bottom + top;
            if (width > maxSide || height > maxSide) return "房间尺寸 " + width + "×" + height + " 超过上限 " + maxSide;
            return null;
        }

        public void Apply()
        {
            var error = Precheck();
            if (error != null) throw new InvalidOperationException(error);
            var draft = holder.Draft;
            oldWidth = draft.Room.Width;
            oldHeight = draft.Room.Height;
            holder.Draft = RoomResize.Resized(draft, left, bottom, oldWidth + left + right, oldHeight + bottom + top);
        }

        public void Revert()
        {
            holder.Draft = RoomResize.Resized(holder.Draft, -left, -bottom, oldWidth, oldHeight);
        }
    }
}
