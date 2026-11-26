/**
 * @file PlacementRules.cs
 * @brief 放置箱子、目标等对象时的纯数据预检，与旧 RoomDraft.PlacementError 的规则与文案一致，供命令与 Unity 侧共用。
 * PlacementRules.Error(draft, categoryId, cell, ignoreId)：返回不能放置的原因，可以放置时返回空；
 * 依次判定：超出房间边界、空格需先铺地板、墙上无法放置、门格或主入口预留格（主入口内侧格为门格减外侧偏移）、格子已被其他对象占用；
 * ignoreId 指定移动时被忽略的对象，使对象能留在原格。
 */
using System.Linq;

namespace Sokoban.Core
{
    public static class PlacementRules
    {
        public static string Error(RoomDraftModel draft, string categoryId, Cell cell, string ignoreId)
        {
            var room = draft.Room;
            if (!room.Inside(cell)) return "超出房间边界";
            string terrain = room.TerrainAt(cell).CategoryId;
            if (terrain == TerrainCategories.Void) return "请先铺设地板";
            if (terrain == TerrainCategories.Wall) return "墙上无法放置";
            var primary = draft.Doors.FirstOrDefault(d => d.Id == draft.PrimaryEntranceId);
            if (draft.Doors.Any(d => d.Cell == cell) || terrain == TerrainCategories.Door ||
                primary != null && cell == new Cell(primary.Cell.X - primary.Outward.X, primary.Cell.Y - primary.Outward.Y))
                return "门格或主入口预留格";
            if (terrain != TerrainCategories.Floor) return "墙上无法放置";
            if (room.Objects.Any(o => o.Id != ignoreId && o.Cell == cell)) return "格子已占用";
            return null;
        }
    }
}
