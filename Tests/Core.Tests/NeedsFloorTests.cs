/**
 * @file NeedsFloorTests.cs
 * @brief 验证 PlacementRules.NeedsFloor：格子上有对象或是任一门的内侧格时必须保留地板，其余格子可以改成墙或擦除。
 * 每个用例以七乘七房间构造草稿，门内侧格取门格减外侧偏移，与旧 RoomDraft 的判定一致。
 */
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class NeedsFloorTests
    {
        static RoomDraftModel Draft(bool secondDoor)
        {
            var room = new Room("r", 7, 7);
            for (int x = 0; x < 7; x++)
                for (int y = 0; y < 7; y++)
                    room.SetTerrain(new Cell(x, y), x == 0 || y == 0 || x == 6 || y == 6 ? TerrainCategories.Wall : TerrainCategories.Floor);
            room.SetTerrain(new Cell(0, 3), TerrainCategories.Door, "entry");
            var doors = new System.Collections.Generic.List<DoorInfo> { new DoorInfo("entry", new Cell(0, 3), Cell.Left, "entry") };
            if (secondDoor)
            {
                room.SetTerrain(new Cell(6, 3), TerrainCategories.Door, "exit");
                doors.Add(new DoorInfo("exit", new Cell(6, 3), Cell.Right, "exit"));
            }
            return new RoomDraftModel(room, doors.ToArray(), "entry");
        }

        [Fact]
        public void FreeFloorCellDoesNotNeedFloor()
        {
            Assert.False(PlacementRules.NeedsFloor(Draft(false), new Cell(3, 3)));
        }

        [Fact]
        public void CellWithObjectNeedsFloor()
        {
            var draft = Draft(false);
            draft.Room.AddObject("box-1", ObjectCategories.Box, new Cell(3, 3));
            Assert.True(PlacementRules.NeedsFloor(draft, new Cell(3, 3)));
        }

        [Fact]
        public void CellInsideADoorNeedsFloor()
        {
            Assert.True(PlacementRules.NeedsFloor(Draft(false), new Cell(1, 3)));
        }

        [Fact]
        public void CellInsideTheSecondaryDoorNeedsFloor()
        {
            var draft = Draft(true);
            Assert.True(PlacementRules.NeedsFloor(draft, new Cell(5, 3)));
            Assert.True(PlacementRules.NeedsFloor(draft, new Cell(1, 3)));
        }

        [Fact]
        public void DoorCellItselfIsNotTheInnerCell()
        {
            Assert.False(PlacementRules.NeedsFloor(Draft(false), new Cell(0, 3)));
        }

        [Fact]
        public void OtherCellsNextToTheDoorDoNotNeedFloor()
        {
            var draft = Draft(false);
            Assert.False(PlacementRules.NeedsFloor(draft, new Cell(1, 2)));
            Assert.False(PlacementRules.NeedsFloor(draft, new Cell(2, 3)));
        }
    }
}
