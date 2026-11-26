using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class OccupiedRuleTests
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
        public void ObjectCellIsOccupied()
        {
            var draft = Draft();
            draft.Room.AddObject("box-1", ObjectCategories.Box, new Cell(3, 3));
            Assert.True(PlacementRules.Occupied(draft, new Cell(3, 3), null));
        }

        [Fact]
        public void IgnoredObjectDoesNotOccupy()
        {
            var draft = Draft();
            draft.Room.AddObject("spawn", ObjectCategories.Spawn, new Cell(3, 3));
            Assert.False(PlacementRules.Occupied(draft, new Cell(3, 3), "spawn"));
        }

        [Fact]
        public void EntranceReserveCellIsNotOccupiedByItself()
        {
            Assert.False(PlacementRules.Occupied(Draft(), new Cell(1, 3), null));
        }

        [Fact]
        public void EmptyCellIsFree()
        {
            Assert.False(PlacementRules.Occupied(Draft(), new Cell(4, 4), null));
        }
    }
}
