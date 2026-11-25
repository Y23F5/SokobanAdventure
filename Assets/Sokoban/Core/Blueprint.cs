/**
 * @file Blueprint.cs
 * @brief 关卡图纸转换器：把字符画设计草图转成类型化 Room，记号只是转换器输入，不进入文件格式。
 * BlueprintResult：转换结果，含房间、主入口内侧的入口缓冲格、门列表。
 * DoorInfo：门的 ID、格子、朝向（向外方向）与角色。
 * Blueprint.Parse(roomId, rows)：rows 按图纸顺序由北向南排列，转换后坐标原点在左下角、y 向北增大。
 * 记号：# 墙，. 地板，_ 空格，P 出生点，B 箱，T 目标，V 可开合地形，< 与 > 为门。
 * 门的朝向由所在位置推出：位于左边缘朝西，右边缘朝东，上边缘朝北，下边缘朝南；< 角色为 entry，> 角色为 exit。
 * 第一个 entry 门为主入口，其内侧格为入口缓冲格。门格下方不可有地板以外的内容，未知记号抛 FormatException。
 */
using System;
using System.Collections.Generic;

namespace Sokoban.Core
{
    public sealed class DoorInfo
    {
        public string Id { get; }
        public Cell Cell { get; }
        public Cell Outward { get; }
        public string Role { get; }

        public DoorInfo(string id, Cell cell, Cell outward, string role)
        {
            Id = id;
            Cell = cell;
            Outward = outward;
            Role = role;
        }
    }

    public sealed class BlueprintResult
    {
        public Room Room { get; }
        public Cell? EntryBuffer { get; }
        public IReadOnlyList<DoorInfo> Doors { get; }

        public BlueprintResult(Room room, Cell? entryBuffer, IReadOnlyList<DoorInfo> doors)
        {
            Room = room;
            EntryBuffer = entryBuffer;
            Doors = doors;
        }
    }

    public static class Blueprint
    {
        public static BlueprintResult Parse(string roomId, string[] rows)
        {
            if (rows == null || rows.Length == 0) throw new FormatException("图纸为空");
            int width = rows[0].Length;
            int height = rows.Length;
            foreach (var r in rows)
                if (r.Length != width) throw new FormatException("图纸各行宽度必须相同");

            var room = new Room(roomId, width, height);
            var doors = new List<DoorInfo>();
            int boxes = 0, targets = 0, vines = 0;

            for (int row = 0; row < height; row++)
            {
                int y = height - 1 - row;
                for (int x = 0; x < width; x++)
                {
                    var cell = new Cell(x, y);
                    char mark = rows[row][x];
                    switch (mark)
                    {
                        case '#': room.SetTerrain(cell, TerrainCategories.Wall); break;
                        case '.': room.SetTerrain(cell, TerrainCategories.Floor); break;
                        case '_': break;
                        case 'P':
                            room.SetTerrain(cell, TerrainCategories.Floor);
                            room.AddObject("spawn", ObjectCategories.Spawn, cell);
                            break;
                        case 'B':
                            room.SetTerrain(cell, TerrainCategories.Floor);
                            room.AddObject("box-" + (++boxes), ObjectCategories.Box, cell);
                            break;
                        case 'T':
                            room.SetTerrain(cell, TerrainCategories.Floor);
                            room.AddObject("target-" + (++targets), ObjectCategories.Target, cell);
                            break;
                        case 'V':
                            room.SetTerrain(cell, TerrainCategories.Openable, "vine-" + (++vines), TerrainCategories.Closed);
                            break;
                        case '<':
                        case '>':
                            var outward = OutwardOf(x, y, width, height);
                            string role = mark == '<' ? "entry" : "exit";
                            string id = role + (doors.FindAll(d => d.Role == role).Count == 0 ? "" : "-" + (doors.Count + 1));
                            room.SetTerrain(cell, TerrainCategories.Door, id);
                            doors.Add(new DoorInfo(id, cell, outward, role));
                            break;
                        default:
                            throw new FormatException($"未知图纸记号 '{mark}' 位于 ({x},{y})");
                    }
                }
            }

            Cell? buffer = null;
            var primary = doors.Find(d => d.Role == "entry");
            if (primary != null) buffer = primary.Cell - primary.Outward;
            return new BlueprintResult(room, buffer, doors);
        }

        static Cell OutwardOf(int x, int y, int width, int height)
        {
            if (x == 0) return Cell.Left;
            if (x == width - 1) return Cell.Right;
            if (y == height - 1) return Cell.Up;
            if (y == 0) return Cell.Down;
            throw new FormatException($"门 ({x},{y}) 不在房间边缘，无法推出朝向");
        }
    }
}
