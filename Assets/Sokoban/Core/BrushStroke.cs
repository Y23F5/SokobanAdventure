/**
 * @file BrushStroke.cs
 * 重做返回同一份笔划结果模型，后续已撤销命令可重新修改它。
 * @brief 整笔预检与整笔提交：给定草稿、一批格子与工具（墙、地板、擦除），先整笔预检，全部通过才产生一条扩建加地形写入的命令，否则整笔拒绝、不改动草稿。
 * 与旧 RoomDraft.Brush 的有意差异（设计文档 7.4 的行为变化）：旧实现遇到非法格会跳过该格并提交其余格，新实现任一格非法则整笔拒绝；
 * 旧实现扩建后才逐格检查，失败时已经扩建，新实现先在副本上预检；旧实现擦除工具会删除箱、目标与装饰对象，新实现擦除只改地形，格上有对象即拒绝；
 * 旧实现地板工具可以覆盖门格，新实现门格对三种工具都受保护；擦除越界不再扩建而是拒绝。
 * BrushTool：Wall、Floor、Erase 三种工具。
 * DraftHolder：持有当前草稿引用，命令替换草稿后外部仍能读到最新草稿。
 * BrushPlan：预检结果，Error 非空表示整笔被拒绝；Left、Bottom、Right、Top 是四向扩建量，Expands 表示是否扩建，NoChange 表示笔划不产生任何改动；CreateCommand(holder) 在预检通过且有改动时返回命令，无改动返回空，被拒绝时抛 InvalidOperationException。
 * BrushPlanner.Preflight(draft, cells, tool, maxSide, rotation)：格子去重后计算越界扩建量并检查尺寸上限，在草稿副本上逐格检查门格保护与 PlacementRules.NeedsFloor（对象格与门内侧格不能改墙或擦除，扩建后按平移后的坐标判定），通过后将笔刷旋转与地形写入副本，擦除不保存旋转；不修改传入的草稿。
 * BrushCommand(holder, cells, tool, maxSide, rotation)：Apply 对持有者当前草稿重新预检，非法或无改动抛 InvalidOperationException，通过后用扩建并写入后的新草稿替换持有者的草稿；Revert 换回原草稿对象，ID 与地形实体随原草稿完整还原。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public enum BrushTool { Wall, Floor, Erase, Openable }

    public sealed class DraftHolder
    {
        public RoomDraftModel Draft;
        public DraftHolder(RoomDraftModel draft) { Draft = draft; }
    }

    public sealed class BrushPlan
    {
        readonly Cell[] cells;
        readonly BrushTool tool;
        readonly int maxSide;
        readonly int rotation;
        readonly Func<Cell, AppearanceRef> appearance;

        public string Error { get; }
        public int Left { get; }
        public int Bottom { get; }
        public int Right { get; }
        public int Top { get; }
        public bool NoChange { get; }
        public bool Expands => Left + Bottom + Right + Top > 0;
        internal RoomDraftModel Result { get; }

        internal BrushPlan(Cell[] cells, BrushTool tool, int maxSide, int rotation, string error, int left, int bottom, int right, int top, bool noChange, RoomDraftModel result, Func<Cell, AppearanceRef> appearance = null)
        {
            this.cells = cells;
            this.tool = tool;
            this.maxSide = maxSide;
            this.rotation = rotation;
            Error = error;
            Left = left;
            Bottom = bottom;
            Right = right;
            Top = top;
            NoChange = noChange;
            Result = result;
            this.appearance = appearance;
        }

        public ICommand CreateCommand(DraftHolder holder)
        {
            if (Error != null) throw new InvalidOperationException(Error);
            return NoChange ? null : new BrushCommand(holder, cells, tool, maxSide, rotation, appearance);
        }
    }

    public static class BrushPlanner
    {
        public static BrushPlan Preflight(RoomDraftModel draft, IEnumerable<Cell> cells, BrushTool tool, int maxSide = int.MaxValue, int rotation = 0, Func<Cell, AppearanceRef> appearance = null)
        {
            var points = cells.Distinct().ToArray();
            if (rotation < 0 || rotation > 3)
                return Rejected(points, tool, maxSide, "旋转档位必须为 0 到 3", 0, 0, 0, 0);
            if (tool == BrushTool.Erase) rotation = 0;
            var room = draft.Room;
            int minX = 0, minY = 0, maxX = room.Width - 1, maxY = room.Height - 1;
            foreach (var point in points)
            {
                if (tool == BrushTool.Erase && !room.Inside(point))
                    return Rejected(points, tool, maxSide, "格子 " + point + " 超出房间，擦除不能扩建", 0, 0, 0, 0);
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
            }
            int left = -minX, bottom = -minY, right = maxX - (room.Width - 1), top = maxY - (room.Height - 1);
            int width = room.Width + left + right, height = room.Height + bottom + top;
            if (width > maxSide || height > maxSide)
                return Rejected(points, tool, maxSide, "房间尺寸 " + width + "×" + height + " 超过上限 " + maxSide, left, bottom, right, top);

            var scratch = RoomResize.Resized(draft, left, bottom, width, height);
            string target = tool == BrushTool.Wall ? TerrainCategories.Wall : tool == BrushTool.Floor ? TerrainCategories.Floor : tool == BrushTool.Openable ? TerrainCategories.Openable : TerrainCategories.Void;
            int writes = 0;
            foreach (var point in points)
            {
                var cell = new Cell(point.X + left, point.Y + bottom);
                var current = scratch.Room.TerrainAt(cell);
                if (current.CategoryId == TerrainCategories.Door || scratch.Doors.Any(door => door.Cell == cell))
                    return Rejected(points, tool, maxSide, "格子 " + point + " 是门格，不能改写", left, bottom, right, top);
                if (tool != BrushTool.Floor && PlacementRules.NeedsFloor(scratch, cell))
                    return Rejected(points, tool, maxSide, "对象或门内侧占用 " + point, left, bottom, right, top);
                var look = tool == BrushTool.Erase ? null : appearance?.Invoke(cell);
                if (current.CategoryId == target)
                {
                    if (current.Rotation == rotation && (look == null || look.Equals(scratch.Room.AppearanceAt(cell)))) continue;
                    scratch.Room.SetTerrain(cell, target, current.EntityId, current.State, rotation);
                }
                else if (tool == BrushTool.Openable) scratch.Room.SetTerrain(cell, target, "vine-" + Guid.NewGuid().ToString("N"), TerrainCategories.Closed, rotation);
                else scratch.Room.SetTerrain(cell, target, rotation: rotation);
                if (look != null) scratch.Room.SetAppearance(cell, look);
                writes++;
            }
            bool expands = left + bottom + right + top > 0;
            return new BrushPlan(points, tool, maxSide, rotation, null, left, bottom, right, top, writes == 0 && !expands, scratch, appearance);
        }

        static BrushPlan Rejected(Cell[] points, BrushTool tool, int maxSide, string error, int left, int bottom, int right, int top) =>
            new BrushPlan(points, tool, maxSide, 0, error, left, bottom, right, top, false, null);
    }

    public sealed class BrushCommand : ICommand
    {
        readonly DraftHolder holder;
        readonly Cell[] cells;
        readonly BrushTool tool;
        readonly int maxSide;
        readonly int rotation;
        readonly Func<Cell, AppearanceRef> appearance;
        RoomDraftModel before, after;

        public BrushCommand(DraftHolder holder, IEnumerable<Cell> cells, BrushTool tool, int maxSide = int.MaxValue, int rotation = 0, Func<Cell, AppearanceRef> appearance = null)
        {
            this.holder = holder;
            this.cells = cells.Distinct().ToArray();
            this.tool = tool;
            this.maxSide = maxSide;
            this.rotation = rotation;
            this.appearance = appearance;
        }

        public string Precheck()
        {
            var plan = BrushPlanner.Preflight(holder.Draft, cells, tool, maxSide, rotation, appearance);
            if (plan.Error != null) return plan.Error;
            return plan.NoChange ? "笔划没有需要修改的格子" : null;
        }

        public void Apply()
        {
            if (after != null && ReferenceEquals(holder.Draft, before))
            {
                holder.Draft = after;
                return;
            }
            var plan = BrushPlanner.Preflight(holder.Draft, cells, tool, maxSide, rotation, appearance);
            if (plan.Error != null) throw new InvalidOperationException(plan.Error);
            if (plan.NoChange) throw new InvalidOperationException("笔划没有需要修改的格子");
            before = holder.Draft;
            holder.Draft = after = plan.Result;
        }

        public void Revert()
        {
            holder.Draft = before;
        }
    }
}
