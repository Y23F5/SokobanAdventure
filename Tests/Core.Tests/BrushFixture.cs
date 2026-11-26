/**
 * @file BrushFixture.cs
 * @brief 整笔预检与草稿平移测试共用的草稿夹具。
 * BrushFixture.Draft()：七乘七房间，外圈为墙、内部为地板，(5,5) 为空格；入口门在 (0,3) 朝西，出口门在 (6,3) 朝东，门地形实体 ID 与门 ID 相同；
 * 箱子 box-1 在 (3,3)，目标 target-1 在 (4,4)，恢复道具 recovery-1，透传字段 note；主入口为 entry。
 */
using Sokoban.Core;

namespace Sokoban.Core.Tests
{
    internal static class BrushFixture
    {
        public static RoomDraftModel Draft()
        {
            var room = new Room("r", 7, 7);
            for (int x = 0; x < 7; x++)
                for (int y = 0; y < 7; y++)
                    room.SetTerrain(new Cell(x, y), x == 0 || y == 0 || x == 6 || y == 6 ? TerrainCategories.Wall : TerrainCategories.Floor);
            room.SetTerrain(new Cell(0, 3), TerrainCategories.Door, "entry");
            room.SetTerrain(new Cell(6, 3), TerrainCategories.Door, "exit");
            room.SetTerrain(new Cell(5, 5), TerrainCategories.Void);
            room.AddObject("box-1", ObjectCategories.Box, new Cell(3, 3));
            room.AddObject("target-1", ObjectCategories.Target, new Cell(4, 4));
            var doors = new[]
            {
                new DoorInfo("entry", new Cell(0, 3), Cell.Left, "entry"),
                new DoorInfo("exit", new Cell(6, 3), Cell.Right, "exit")
            };
            var draft = new RoomDraftModel(room, doors, "entry");
            new AddRecoveryItemCommand(draft, new RecoveryItem("recovery-1", "warehouse-lift", 3.7f)).Apply();
            draft.Extras["note"] = "x";
            return draft;
        }
    }
}
