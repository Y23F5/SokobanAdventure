/**
 * @file Room.cs
 * @brief Room 是房间的类型化编辑模型：每格一条地形记录的网格，加每格最多一个对象的对象表，对象与地形实体共用每房唯一的 ID 空间。
 * TerrainCell：一格地形的数据（类别 ID、可选实体 ID、初始状态）。
 * PlacedObject：一个对象（ID、类别 ID、格子）。
 * Room(id, width, height)：构造全空格（void）的房间。
 * TerrainAt(cell)、Inside(cell)：读取地形与判断是否在房间矩形内，越界读取返回 void 记录。
 * SetTerrain(cell, categoryId, entityId, state)：写入一格地形，实体 ID 非空时登记到 ID 空间。
 * AddObject(id, categoryId, cell)：登记对象，ID 重复抛 ArgumentException。
 * RemoveObject(id)：移除对象并释放 ID。
 * ObjectAt(cell)、ObjectsOf(categoryId)：按格子与类别查询对象。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class TerrainCell
    {
        public string CategoryId { get; }
        public string EntityId { get; }
        public string State { get; }

        public TerrainCell(string categoryId, string entityId = null, string state = null)
        {
            CategoryId = categoryId;
            EntityId = entityId;
            State = state;
        }
    }

    public sealed class PlacedObject
    {
        public string Id { get; }
        public string CategoryId { get; }
        public Cell Cell { get; }

        public PlacedObject(string id, string categoryId, Cell cell)
        {
            Id = id;
            CategoryId = categoryId;
            Cell = cell;
        }
    }

    public sealed class Room
    {
        static readonly TerrainCell VoidCell = new TerrainCell(TerrainCategories.Void);

        readonly TerrainCell[,] grid;
        readonly List<PlacedObject> objects = new List<PlacedObject>();

        public string Id { get; }
        public int Width { get; }
        public int Height { get; }
        public IdSpace Ids { get; }
        public IReadOnlyList<PlacedObject> Objects => objects;

        public Room(string id, int width, int height)
        {
            if (width <= 0 || height <= 0) throw new ArgumentException("房间尺寸必须为正");
            Id = id;
            Width = width;
            Height = height;
            Ids = new IdSpace(id);
            grid = new TerrainCell[width, height];
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    grid[x, y] = VoidCell;
        }

        public bool Inside(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;

        public TerrainCell TerrainAt(Cell cell) => Inside(cell) ? grid[cell.X, cell.Y] : VoidCell;

        public void SetTerrain(Cell cell, string categoryId, string entityId = null, string state = null)
        {
            if (!Inside(cell)) throw new ArgumentOutOfRangeException(nameof(cell), $"格子 {cell} 超出房间");
            var previous = grid[cell.X, cell.Y];
            if (previous.EntityId != null && previous.EntityId != entityId) Ids.Remove(previous.EntityId);
            if (entityId != null && previous.EntityId != entityId) Ids.Add(entityId, "terrain");
            grid[cell.X, cell.Y] = new TerrainCell(categoryId, entityId, state);
        }

        public PlacedObject AddObject(string id, string categoryId, Cell cell)
        {
            Ids.Add(id, categoryId);
            var placed = new PlacedObject(id, categoryId, cell);
            objects.Add(placed);
            return placed;
        }

        public bool RemoveObject(string id)
        {
            int index = objects.FindIndex(o => o.Id == id);
            if (index < 0) return false;
            objects.RemoveAt(index);
            Ids.Remove(id);
            return true;
        }

        public PlacedObject ObjectAt(Cell cell) => objects.FirstOrDefault(o => o.Cell == cell);

        public IEnumerable<PlacedObject> ObjectsOf(string categoryId) => objects.Where(o => o.CategoryId == categoryId);
    }
}
