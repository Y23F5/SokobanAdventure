/**
 * @file LegacyRoomAdapter.cs
 * @brief 旧房间数据到 Core 草稿模型的转换：输入是 Unity 侧从旧 JObject 房间抽出的普通数据，Core 不引用 Newtonsoft。
 * LegacyEntity：旧房间里带 ID 与格子的实体（箱子、目标、出生点），箱子可带可选的恢复格 RecoveryCell。
 * LegacyDoor：旧房间里的门（ID、格子、方向字母 N/E/S/W、角色）。
 * LegacyDecoration：旧房间里的装饰物（ID、格子、可选的装饰类型 Kind 与外观引用 AppearanceRef，缺省为空）。
 * LegacyRoomData：旧房间拆出的数据（房间 ID、尺寸、由北向南的地形行、门、主入口、箱、目标、出生点、装饰物 Decorations，以及 string 到 string 的透传字段包 Extras，Core 不解释，默认为空，可为 null）。
 * LegacyRoomAdapter.ToDraft(data)：行序转为原点在左下角，# 为墙，. 为地板，_ 为空格；门格改写为 door 地形并以门 ID 作实体 ID；
 * 方向字母转为向外偏移（N 上、E 右、S 下、W 左）；箱、目标、出生点转为对象，装饰物转为 custom 类别对象并保留装饰类型与外观引用，Extras 原样复制进草稿的 Extras。未知地形字符、未知方向、行宽或行数与尺寸不符、ID 为空、仅含空白或重复（含门、对象与装饰同名）均抛 FormatException。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class LegacyEntity
    {
        public string Id { get; }
        public Cell Cell { get; }
        public Cell? RecoveryCell { get; }

        public LegacyEntity(string id, Cell cell, Cell? recoveryCell = null)
        {
            Id = id;
            Cell = cell;
            RecoveryCell = recoveryCell;
        }
    }

    public sealed class LegacyDoor
    {
        public string Id { get; }
        public Cell Cell { get; }
        public string Direction { get; }
        public string Role { get; }

        public LegacyDoor(string id, Cell cell, string direction, string role)
        {
            Id = id;
            Cell = cell;
            Direction = direction;
            Role = role;
        }
    }

    public sealed class LegacyDecoration
    {
        public string Id { get; }
        public Cell Cell { get; }
        public string Kind { get; }
        public string AppearanceRef { get; }

        public LegacyDecoration(string id, Cell cell, string kind = null, string appearanceRef = null)
        {
            Id = id;
            Cell = cell;
            Kind = kind;
            AppearanceRef = appearanceRef;
        }
    }

    public sealed class LegacyRoomData
    {
        public string LevelId { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string[] RowsNorthToSouth { get; set; }
        public LegacyDoor[] Doors { get; set; } = new LegacyDoor[0];
        public string PrimaryEntranceId { get; set; }
        public LegacyEntity[] Boxes { get; set; } = new LegacyEntity[0];
        public LegacyEntity[] Targets { get; set; } = new LegacyEntity[0];
        public LegacyEntity Spawn { get; set; }
        public LegacyDecoration[] Decorations { get; set; } = new LegacyDecoration[0];
        public Dictionary<string, string> Extras { get; set; } = new Dictionary<string, string>();
    }

    public static class LegacyRoomAdapter
    {
        public static RoomDraftModel ToDraft(LegacyRoomData data)
        {
            var rows = data.RowsNorthToSouth;
            if (rows == null || rows.Length != data.Height || rows.Any(r => r == null || r.Length != data.Width))
                throw new FormatException("地形行与房间尺寸不一致");
            var room = new Room(data.LevelId, data.Width, data.Height);
            for (int row = 0; row < data.Height; row++)
            {
                int y = data.Height - 1 - row;
                for (int x = 0; x < data.Width; x++)
                {
                    var cell = new Cell(x, y);
                    switch (rows[row][x])
                    {
                        case '#': room.SetTerrain(cell, TerrainCategories.Wall); break;
                        case '.': room.SetTerrain(cell, TerrainCategories.Floor); break;
                        case '_': break;
                        default: throw new FormatException("未知地形字符 '" + rows[row][x] + "' 位于 " + cell);
                    }
                }
            }

            RequireIds(data);
            var doors = new List<DoorInfo>();
            try
            {
                foreach (var door in data.Doors)
                {
                    if (!room.Inside(door.Cell)) throw new FormatException("门 " + door.Id + " 的格子 " + door.Cell + " 超出房间");
                    room.SetTerrain(door.Cell, TerrainCategories.Door, door.Id);
                    doors.Add(new DoorInfo(door.Id, door.Cell, Outward(door.Direction), door.Role));
                }

                foreach (var box in data.Boxes) room.AddObject(box.Id, ObjectCategories.Box, box.Cell, box.RecoveryCell);
                foreach (var target in data.Targets) room.AddObject(target.Id, ObjectCategories.Target, target.Cell);
                if (data.Spawn != null) room.AddObject(data.Spawn.Id, ObjectCategories.Spawn, data.Spawn.Cell);
                if (data.Decorations != null)
                    foreach (var deco in data.Decorations) room.AddObject(deco.Id, ObjectCategories.Custom, deco.Cell, null, deco.Kind, deco.AppearanceRef);
            }
            catch (ArgumentException e)
            {
                throw new FormatException(e.Message, e);
            }
            return new RoomDraftModel(room, doors, data.PrimaryEntranceId, data.Extras);
        }

        static void RequireIds(LegacyRoomData data)
        {
            var ids = data.Doors.Select(d => d.Id).Concat(data.Boxes.Select(b => b.Id)).Concat(data.Targets.Select(t => t.Id))
                .Concat((data.Decorations ?? new LegacyDecoration[0]).Select(d => d.Id));
            if (data.Spawn != null) ids = ids.Append(data.Spawn.Id);
            if (ids.Any(string.IsNullOrWhiteSpace)) throw new FormatException("ID 为空或仅含空白");
        }

        static Cell Outward(string direction)
        {
            switch (direction)
            {
                case "N": return Cell.Up;
                case "E": return Cell.Right;
                case "S": return Cell.Down;
                case "W": return Cell.Left;
                default: throw new FormatException("门朝向无效：" + direction);
            }
        }
    }
}
