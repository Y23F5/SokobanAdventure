/**
 * @file PushRules.cs
 * @brief 推动与解开判定的纯规则：箱子只能被推向可通行且无其他箱子的格，恢复期间一律禁推，解开条件是每个目标上都有箱子。
 * TryPush(room, session, boxes, player, direction, out result)：玩家向 direction 移动一步；目标格有箱子时尝试把箱子推向下一格，
 * 下一格必须在房间内、可通行、不是其他箱子；恢复期间禁推；推动不拉动；成功后返回新的玩家格与箱位。
 * Solved(room, boxes)：目标数至少为 1，且每个目标格上都有箱子，允许箱数多于目标数。
 * PushResult：Moved 表示玩家是否移动，Pushed 表示是否推动了箱子，PushedBoxId 为被推箱子 ID。
 */
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public readonly struct PushResult
    {
        public readonly bool Moved;
        public readonly bool Pushed;
        public readonly string PushedBoxId;
        public readonly Cell Player;

        public PushResult(bool moved, bool pushed, string pushedBoxId, Cell player)
        {
            Moved = moved;
            Pushed = pushed;
            PushedBoxId = pushedBoxId;
            Player = player;
        }
    }

    public static class PushRules
    {
        public static PushResult TryPush(RecoverySession session, Cell player, Cell direction)
        {
            var next = player + direction;
            if (!session.IsWalkable(next) || !session.CanEnter(next))
                return new PushResult(false, false, null, player);

            string boxId = null;
            foreach (var b in session.Boxes)
                if (b.Value == next) { boxId = b.Key; break; }

            if (boxId == null) return new PushResult(true, false, null, next);

            if (!session.CanPush()) return new PushResult(false, false, null, player);
            var beyond = next + direction;
            if (!session.IsWalkable(beyond) || session.Boxes.ContainsValue(beyond))
                return new PushResult(false, false, null, player);

            session.Boxes[boxId] = beyond;
            return new PushResult(true, true, boxId, next);
        }

        public static bool Solved(Room room, IDictionary<string, Cell> boxes)
        {
            var targets = room.ObjectsOf(ObjectCategories.Target).ToList();
            if (targets.Count == 0) return false;
            var occupied = new HashSet<Cell>(boxes.Values);
            return targets.All(t => occupied.Contains(t.Cell));
        }
    }
}
