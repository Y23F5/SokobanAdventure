using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class DoorPlacementTests
    {
        static Room Floor5()
        {
            var room = new Room("r", 7, 7);
            for (int x = 1; x < 6; x++)
                for (int y = 1; y < 6; y++)
                    room.SetTerrain(new Cell(x, y), TerrainCategories.Floor);
            return room;
        }

        [Fact]
        public void WestEdgeFloorFacesLeft()
        {
            Assert.Equal((Cell?)Cell.Left, DoorPlacement.Outward(Floor5(), new Cell(1, 3)));
        }

        [Fact]
        public void EastEdgeFloorFacesRight()
        {
            Assert.Equal((Cell?)Cell.Right, DoorPlacement.Outward(Floor5(), new Cell(5, 3)));
        }

        [Fact]
        public void NorthEdgeFloorFacesUp()
        {
            Assert.Equal((Cell?)Cell.Up, DoorPlacement.Outward(Floor5(), new Cell(3, 5)));
        }

        [Fact]
        public void SouthEdgeFloorFacesDown()
        {
            Assert.Equal((Cell?)Cell.Down, DoorPlacement.Outward(Floor5(), new Cell(3, 1)));
        }

        [Fact]
        public void NorthWinsWhenTwoSidesQualify()
        {
            Assert.Equal((Cell?)Cell.Up, DoorPlacement.Outward(Floor5(), new Cell(1, 5)));
        }

        [Fact]
        public void InteriorCellHasNoDirection()
        {
            Assert.Null(DoorPlacement.Outward(Floor5(), new Cell(3, 3)));
        }
    }
}
