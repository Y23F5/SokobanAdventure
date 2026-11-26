using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RecoveryCellCarryTests
    {
        static Room Base()
        {
            var room = new Room("r", 6, 6);
            for (int x = 0; x < 6; x++)
                for (int y = 0; y < 6; y++)
                    room.SetTerrain(new Cell(x, y), x == 0 || y == 0 || x == 5 || y == 5 ? TerrainCategories.Wall : TerrainCategories.Floor);
            return room;
        }

        [Fact]
        public void ResizedShiftsRecoveryCellWithTheBox()
        {
            var room = Base();
            room.AddObject("box-1", ObjectCategories.Box, new Cell(2, 2), new Cell(3, 3));
            room.AddObject("box-2", ObjectCategories.Box, new Cell(2, 3));
            var next = RoomResize.Resized(room, 1, 2, 8, 9);
            Assert.Equal((Cell?)new Cell(4, 5), next.ObjectAt(new Cell(3, 4)).RecoveryCell);
            Assert.Null(next.ObjectAt(new Cell(3, 5)).RecoveryCell);
        }

        [Fact]
        public void ExpandThenRevertRestoresRecoveryCell()
        {
            var holder = new RoomHolder(Base());
            holder.Room.AddObject("box-1", ObjectCategories.Box, new Cell(2, 2), new Cell(3, 3));
            var cmd = new ExpandCommand(holder, 1, 1, 1, 1);
            cmd.Apply();
            Assert.Equal((Cell?)new Cell(4, 4), holder.Room.ObjectAt(new Cell(3, 3)).RecoveryCell);
            cmd.Revert();
            Assert.Equal((Cell?)new Cell(3, 3), holder.Room.ObjectAt(new Cell(2, 2)).RecoveryCell);
        }

        [Fact]
        public void AdapterCarriesLegacyBoxRecoveryCell()
        {
            var rows = new[] { "#####", "#...#", "#...#", "#...#", "#####" };
            var data = new LegacyRoomData
            {
                LevelId = "r", Width = 5, Height = 5, RowsNorthToSouth = rows,
                Boxes = new[] { new LegacyEntity("box-1", new Cell(1, 1), new Cell(3, 3)), new LegacyEntity("box-2", new Cell(2, 1)) },
            };
            var room = LegacyRoomAdapter.ToDraft(data).Room;
            Assert.Equal((Cell?)new Cell(3, 3), room.ObjectAt(new Cell(1, 1)).RecoveryCell);
            Assert.Null(room.ObjectAt(new Cell(2, 1)).RecoveryCell);
        }
    }
}
