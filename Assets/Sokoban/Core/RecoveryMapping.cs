/**
 * @file RecoveryMapping.cs
 * @brief 旧房间的恢复演出名到 Core 恢复道具的映射，把旧的 recoveryPresentation 字符串变成带脚本引用与参数的恢复道具列表。
 * RecoveryMapping.FromPresentation(preset, draft)：warehouse 对应 warehouse-lift，factory 对应 factory-belt，forest 对应 forest-decay 加 forest-vine，未知名称返回空列表；
 * 藤门沿用旧预览的摆法：取角色为 exit 的门，内侧为外侧取反，侧向为 (内侧.y, -内侧.x)，候选墙格先取门格加侧向，不是墙则改取门格减侧向，仍不是墙则不生成藤门；
 * 藤门参数 gateX、gateZ 为墙格坐标沿内侧偏移 0.70，yaw 为内侧方向绕竖直轴的角度（度），与旧预览的 LookRotation 一致。
 * 每个恢复道具时长取默认 3.7 秒，ID 依次为 recovery-1、recovery-2。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public static class RecoveryMapping
    {
        const float DefaultDuration = 3.7f;
        const float VineOffset = 0.70f;

        public static IReadOnlyList<RecoveryItem> FromPresentation(string preset, RoomDraftModel draft)
        {
            var items = new List<RecoveryItem>();
            switch (preset)
            {
                case "warehouse":
                    items.Add(Item(items.Count, "warehouse-lift"));
                    break;
                case "factory":
                    items.Add(Item(items.Count, "factory-belt"));
                    break;
                case "forest":
                    items.Add(Item(items.Count, "forest-decay"));
                    var vine = VineParams(draft);
                    if (vine != null) items.Add(Item(items.Count, "forest-vine", vine));
                    break;
            }
            return items;
        }

        static RecoveryItem Item(int index, string script, IReadOnlyDictionary<string, float> parameters = null) =>
            new RecoveryItem("recovery-" + (index + 1), script, DefaultDuration, parameters);

        static IReadOnlyDictionary<string, float> VineParams(RoomDraftModel draft)
        {
            var exit = draft.Doors.FirstOrDefault(d => d.Role == "exit");
            if (exit == null) return null;
            var room = draft.Room;
            var inward = new Cell(-exit.Outward.X, -exit.Outward.Y);
            var side = new Cell(inward.Y, -inward.X);
            var cell = new Cell(exit.Cell.X + side.X, exit.Cell.Y + side.Y);
            if (room.TerrainAt(cell).CategoryId != TerrainCategories.Wall) cell = new Cell(exit.Cell.X - side.X, exit.Cell.Y - side.Y);
            if (room.TerrainAt(cell).CategoryId != TerrainCategories.Wall) return null;
            return new Dictionary<string, float>(StringComparer.Ordinal)
            {
                ["gateX"] = cell.X + inward.X * VineOffset,
                ["gateZ"] = cell.Y + inward.Y * VineOffset,
                ["yaw"] = (float)(Math.Atan2(inward.X, inward.Y) * 180.0 / Math.PI),
            };
        }
    }
}
