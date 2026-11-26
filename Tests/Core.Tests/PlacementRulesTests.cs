using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class PlacementRulesTests
    {
        static RoomDraftModel Draft()
        {
            var room = new Room("r", 7, 7);
            for (int x = 0; x < 7; x++)
                for (int y = 0; y < 7; y++)
                    room.SetTerrain(new Cell(x, y), x == 0 || y == 0 || x == 6 || y == 6 ? TerrainCategories.Wall : TerrainCategories.Floor);
            room.SetTerrain(new Cell(0, 3), TerrainCategories.Door, "entry");
            var doors = new[] { new DoorInfo("entry", new Cell(0, 3), Cell.Left, "entry") };
            return new RoomDraftModel(room, doors, "entry");
        }

        [Fact]
        public void FreeFloorCellHasNoError()
        {
            Assert.Null(PlacementRules.Error(Draft(), ObjectCategories.Box, new Cell(3, 3), null));
        }

        [Fact]
        public void OutsideTheRoomIsRejected()
        {
            Assert.Equal("超出房间边界", PlacementRules.Error(Draft(), ObjectCategories.Box, new Cell(9, 9), null));
        }

        [Fact]
        public void VoidCellAsksForFloorFirst()
        {
            var draft = Draft();
            draft.Room.SetTerrain(new Cell(3, 3), TerrainCategories.Void);
            Assert.Equal("请先铺设地板", PlacementRules.Error(draft, ObjectCategories.Box, new Cell(3, 3), null));
        }

        [Fact]
        public void WallCellIsRejected()
        {
            Assert.Equal("墙上无法放置", PlacementRules.Error(Draft(), ObjectCategories.Box, new Cell(0, 1), null));
        }

        [Fact]
        public void DoorCellIsRejected()
        {
            Assert.Equal("门格或主入口预留格", PlacementRules.Error(Draft(), ObjectCategories.Box, new Cell(0, 3), null));
        }

        [Fact]
        public void CellInsideThePrimaryEntranceIsRejected()
        {
            Assert.Equal("门格或主入口预留格", PlacementRules.Error(Draft(), ObjectCategories.Box, new Cell(1, 3), null));
        }

        [Fact]
        public void OccupiedCellIsRejectedUnlessIgnored()
        {
            var draft = Draft();
            draft.Room.AddObject("box-1", ObjectCategories.Box, new Cell(3, 3));
            Assert.Equal("格子已占用", PlacementRules.Error(draft, ObjectCategories.Box, new Cell(3, 3), null));
            Assert.Null(PlacementRules.Error(draft, ObjectCategories.Box, new Cell(3, 3), "box-1"));
        }
    }
}
