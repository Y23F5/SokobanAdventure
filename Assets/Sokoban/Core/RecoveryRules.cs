/**
 * @file RecoveryRules.cs
 * @brief 恢复规则的纯函数：占用区计算与玩家安全移位，与恢复方法无关。
 * OccupiedZone(boxes)：按每个箱子的当前格、初始格及两格之间先沿 x 再沿 y 的路线格求并集，可开合地形格不在其内。
 * Route(from, to)：返回含两端的路线格，先沿 x 走到目标列，再沿 y 走到目标行。
 * SafeCell(start, walkable, forbidden, boxCells, entryBuffer)：从玩家当前格做广度优先，邻格顺序固定为上、右、下、左，取最先确认的合法格；
 * 搜索不到时传送到入口缓冲格，缓冲格被箱子占住或在禁入集内时，改取距离最近的合法格，距离相同按固定顺序。
 * 合法格：可通行、不在禁入集、不在箱子所在格。
 */
using System;
using System.Collections.Generic;

namespace Sokoban.Core
{
    public readonly struct BoxMove
    {
        public readonly Cell Current;
        public readonly Cell Initial;
        public BoxMove(Cell current, Cell initial) { Current = current; Initial = initial; }
    }

    public static class RecoveryRules
    {
        public static List<Cell> Route(Cell from, Cell to)
        {
            var cells = new List<Cell> { from };
            var at = from;
            int stepX = Math.Sign(to.X - from.X);
            while (at.X != to.X) { at = new Cell(at.X + stepX, at.Y); cells.Add(at); }
            int stepY = Math.Sign(to.Y - from.Y);
            while (at.Y != to.Y) { at = new Cell(at.X, at.Y + stepY); cells.Add(at); }
            return cells;
        }

        public static HashSet<Cell> OccupiedZone(IEnumerable<BoxMove> boxes)
        {
            var zone = new HashSet<Cell>();
            foreach (var box in boxes)
                foreach (var cell in Route(box.Current, box.Initial)) zone.Add(cell);
            return zone;
        }

        public static Cell SafeCell(Cell start, Func<Cell, bool> walkable, ISet<Cell> forbidden,
            ISet<Cell> boxCells, Cell? entryBuffer)
        {
            bool Legal(Cell c) => walkable(c) && !forbidden.Contains(c) && !boxCells.Contains(c);

            var queue = new Queue<Cell>();
            var seen = new HashSet<Cell> { start };
            queue.Enqueue(start);
            if (Legal(start)) return start;
            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                foreach (var d in Cell.SearchOrder)
                {
                    var next = at + d;
                    if (!seen.Add(next) || !walkable(next)) continue;
                    if (Legal(next)) return next;
                    queue.Enqueue(next);
                }
            }

            if (entryBuffer.HasValue)
            {
                var buffer = entryBuffer.Value;
                if (Legal(buffer)) return buffer;
                var nearest = NearestLegal(buffer, walkable, Legal);
                if (nearest.HasValue) return nearest.Value;
                return buffer;
            }
            return start;
        }

        static Cell? NearestLegal(Cell from, Func<Cell, bool> walkable, Func<Cell, bool> legal)
        {
            var queue = new Queue<Cell>();
            var seen = new HashSet<Cell> { from };
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                foreach (var d in Cell.SearchOrder)
                {
                    var next = at + d;
                    if (!seen.Add(next) || !walkable(next)) continue;
                    if (legal(next)) return next;
                    queue.Enqueue(next);
                }
            }
            return null;
        }
    }
}
