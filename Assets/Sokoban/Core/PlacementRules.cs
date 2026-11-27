/**
 * @file PlacementRules.cs
 * @brief 放置箱子、目标等对象时的纯数据预检，与旧 RoomDraft.PlacementError 的规则与文案一致，供命令与 Unity 侧共用。
 * PlacementRules.Error(draft, categoryId, cell, ignoreId)：返回不能放置的原因，可以放置时返回空；
 * 依次判定：超出房间边界、空格需先铺地板、墙上无法放置、门格或主入口预留格（主入口内侧格为门格减外侧偏移）、格子已被其他对象占用；
 * ignoreId 指定移动时被忽略的对象，使对象能留在原格；
 * 出生点类别不受主入口预留格限制（出生点固定在主入口内侧格）；
 * PlacementRules.Occupied(draft, cell, ignoreId)：只判定格子是否被其他对象占用，不含门格与主入口预留格，供设置出生点与门时使用；装饰物以 custom 类别对象存放在对象表中，与箱、目标一样参与占用判定；
 * PlacementRules.NeedsFloor(draft, cell)：判定格子是否必须保留地板，即格子上有任意对象，或格子是任一扇门的内侧格（门格减外侧偏移），供改墙与擦除地形前检查；只看对象表与门表，不涉及已删除的恢复格。
 */
using System.Linq;

namespace Sokoban.Core
{
    public static class PlacementRules
    {
        public static bool Occupied(RoomDraftModel draft, Cell cell, string ignoreId) =>
            draft.Room.Objects.Any(o => o.Id != ignoreId && o.Cell == cell);

        public static bool NeedsFloor(RoomDraftModel draft, Cell cell) =>
            draft.Room.Objects.Any(o => o.Cell == cell) ||
            draft.Doors.Any(d => cell == new Cell(d.Cell.X - d.Outward.X, d.Cell.Y - d.Outward.Y));

        public static string Error(RoomDraftModel draft, string categoryId, Cell cell, string ignoreId)
        {
            var room = draft.Room;
            if (room.IsRecoveryDeviceFloor(cell)) return "恢复装置地板不可放置";
            if (!room.Inside(cell)) return "超出房间边界";
            string terrain = room.TerrainAt(cell).CategoryId;
            if (terrain == TerrainCategories.Void) return "请先铺设地板";
            if (terrain == TerrainCategories.Wall) return "墙上无法放置";
            var primary = draft.Doors.FirstOrDefault(d => d.Id == draft.PrimaryEntranceId);
            if (draft.Doors.Any(d => d.Cell == cell) || terrain == TerrainCategories.Door ||
                categoryId != ObjectCategories.Spawn && primary != null && cell == new Cell(primary.Cell.X - primary.Outward.X, primary.Cell.Y - primary.Outward.Y))
                return "门格或主入口预留格";
            if (terrain != TerrainCategories.Floor) return "墙上无法放置";
            if (room.Objects.Any(o => o.Id != ignoreId && o.Cell == cell)) return "格子已占用";
            return null;
        }
    }
}
