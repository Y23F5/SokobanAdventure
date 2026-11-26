/**
 * @file DoorPlacement.cs
 * @brief 在地板格上设置门时选择朝向的纯规则，与旧 RoomDraft.SetRoomMarker 的选向一致。
 * DoorPlacement.Outward(room, cell)：按上、右、下、左的顺序，返回第一个满足“外侧格为空格且内侧格为地板”的外侧偏移，都不满足时返回空；
 * 格子自身是否为地板、是否已被占用由调用方先判定。
 */
namespace Sokoban.Core
{
    public static class DoorPlacement
    {
        static readonly Cell[] Order = { Cell.Up, Cell.Right, Cell.Down, Cell.Left };

        public static Cell? Outward(Room room, Cell cell)
        {
            foreach (var outward in Order)
            {
                var outside = new Cell(cell.X + outward.X, cell.Y + outward.Y);
                var inside = new Cell(cell.X - outward.X, cell.Y - outward.Y);
                if (room.TerrainAt(outside).CategoryId == TerrainCategories.Void &&
                    room.TerrainAt(inside).CategoryId == TerrainCategories.Floor)
                    return outward;
            }
            return null;
        }
    }
}
