/**
 * @file WorldValidator.cs
 * @brief 世界级放置检查的纯几何规则，与旧 WorldLayout.PlacementIssue 的判定与文案一致，供世界编辑与世界校验共用。
 * WorldValidator.Placement(world, sizes, id, origin, moving, size)：返回不能放置的原因，可以放置时返回空；
 * 先判世界边界（房间占地超出 Min 与 Max 时给出首个冲突格），再逐个比较已放置房间：同 ID 时 moving 为真则跳过、否则报已放置，
 * 占地矩形相交时报两房重叠并给出两房原点逐轴取大的冲突格，仅边相接不算重叠；sizes 为各房间的宽高，按房间 ID 查找。
 */
using System;
using System.Collections.Generic;

namespace Sokoban.Core
{
    public static class WorldValidator
    {
        public static string Placement(WorldDocument world, IReadOnlyDictionary<string, Cell> sizes, string id, Cell origin, bool moving, Cell size)
        {
            long right = (long)origin.X + size.X, top = (long)origin.Y + size.Y;
            if (origin.X < world.Min.X || origin.Y < world.Min.Y || right - 1 > world.Max.X || top - 1 > world.Max.Y)
            {
                long outsideX = origin.X;
                long outsideY = origin.Y;
                if (origin.X >= world.Min.X && origin.Y >= world.Min.Y)
                {
                    if (right - 1 > world.Max.X) outsideX = Math.Max(outsideX, (long)world.Max.X + 1);
                    else outsideY = Math.Max(outsideY, (long)world.Max.Y + 1);
                }
                return id + " 超出世界边界，冲突格 (" + outsideX + ", " + outsideY + ")";
            }
            foreach (var other in world.Rooms)
            {
                if (other.LevelId == id)
                {
                    if (moving) continue;
                    return id + " 已放置";
                }
                var pos = other.Origin;
                var extent = sizes[other.LevelId];
                if (origin.X < (long)pos.X + extent.X && right > pos.X && origin.Y < (long)pos.Y + extent.Y && top > pos.Y)
                    return id + " 与 " + other.LevelId + " 占地重叠 (" + Math.Max(origin.X, pos.X) + ", " + Math.Max(origin.Y, pos.Y) + ")";
            }
            return null;
        }
    }
}
