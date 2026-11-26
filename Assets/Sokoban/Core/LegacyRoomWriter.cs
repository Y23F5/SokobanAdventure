/**
 * @file LegacyRoomWriter.cs
 * @brief Core 草稿模型到旧房间数据的反向转换，与 LegacyRoomAdapter 互为往返；只覆盖 Core 管辖的结构字段，旧 JObject 里 Core 不认识的字段由调用方原样保留。
 * LegacyRoomWriter.FromDraft(draft)：地形按由北向南重建行，墙为 #，地板与门格为 .，空格为 _；
 * 门的外侧偏移转回方向字母（上 N、右 E、下 S、左 W），无法对应时抛 FormatException；
 * 箱子带恢复格，目标与出生点按类别取出，出生点缺失时返回空；箱与目标按 ID 的序数顺序输出以保证结果稳定；装饰物（custom 类别对象）按登记顺序输出并带装饰类型与外观引用；草稿的 Extras 原样复制回输出。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sokoban.Core
{
    public static class LegacyRoomWriter
    {
        public static LegacyRoomData FromDraft(RoomDraftModel draft)
        {
            var room = draft.Room;
            var rows = new string[room.Height];
            for (int row = 0; row < room.Height; row++)
            {
                int y = room.Height - 1 - row;
                var line = new StringBuilder(room.Width);
                for (int x = 0; x < room.Width; x++)
                {
                    string category = room.TerrainAt(new Cell(x, y)).CategoryId;
                    line.Append(category == TerrainCategories.Wall ? '#'
                        : category == TerrainCategories.Floor || category == TerrainCategories.Door ? '.' : '_');
                }
                rows[row] = line.ToString();
            }

            LegacyEntity Entity(PlacedObject o) => new LegacyEntity(o.Id, o.Cell, o.RecoveryCell);
            var spawn = room.ObjectsOf(ObjectCategories.Spawn).FirstOrDefault();
            return new LegacyRoomData
            {
                LevelId = room.Id,
                Width = room.Width,
                Height = room.Height,
                RowsNorthToSouth = rows,
                Doors = draft.Doors.Select(d => new LegacyDoor(d.Id, d.Cell, Letter(d.Outward), d.Role)).ToArray(),
                PrimaryEntranceId = draft.PrimaryEntranceId,
                Boxes = room.ObjectsOf(ObjectCategories.Box).OrderBy(o => o.Id, StringComparer.Ordinal).Select(Entity).ToArray(),
                Targets = room.ObjectsOf(ObjectCategories.Target).OrderBy(o => o.Id, StringComparer.Ordinal).Select(Entity).ToArray(),
                Spawn = spawn == null ? null : Entity(spawn),
                Decorations = room.ObjectsOf(ObjectCategories.Custom).Select(o => new LegacyDecoration(o.Id, o.Cell, o.DecorationKind, o.AppearanceRef)).ToArray(),
                Extras = new Dictionary<string, string>(draft.Extras, StringComparer.Ordinal),
            };
        }

        static string Letter(Cell outward)
        {
            if (outward.Equals(Cell.Up)) return "N";
            if (outward.Equals(Cell.Right)) return "E";
            if (outward.Equals(Cell.Down)) return "S";
            if (outward.Equals(Cell.Left)) return "W";
            throw new FormatException("门的外侧偏移无法对应方向：" + outward);
        }
    }
}
