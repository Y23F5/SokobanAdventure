using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class LegacyRoomAdapterTests
    {
        static readonly string[] Rows =
        {
            "#########",
            "#.......#",
            "#.......#",
            ".........",
            "#.......#",
            "#.......#",
            "#########",
        };

        static LegacyRoomData Warehouse() => new LegacyRoomData
        {
            LevelId = "warehouse",
            Width = 9,
            Height = 7,
            RowsNorthToSouth = Rows,
            Doors = new[]
            {
                new LegacyDoor("entry", new Cell(0, 3), "W", "entry"),
                new LegacyDoor("exit", new Cell(8, 3), "E", "exit"),
            },
            PrimaryEntranceId = "entry",
            Boxes = new[] { new LegacyEntity("box-1", new Cell(4, 2)) },
            Targets = new[] { new LegacyEntity("target-1", new Cell(6, 2)) },
            Spawn = new LegacyEntity("spawn", new Cell(2, 2)),
        };

        [Fact]
        public void DoorCellsBecomeDoorTerrain()
        {
            var draft = LegacyRoomAdapter.ToDraft(Warehouse());
            Assert.Equal(TerrainCategories.Door, draft.Room.TerrainAt(new Cell(0, 3)).CategoryId);
            Assert.Equal(TerrainCategories.Door, draft.Room.TerrainAt(new Cell(8, 3)).CategoryId);
        }

        [Fact]
        public void RowsAreReadNorthToSouthWithOriginAtBottomLeft()
        {
            var draft = LegacyRoomAdapter.ToDraft(Warehouse());
            Assert.Equal(TerrainCategories.Wall, draft.Room.TerrainAt(new Cell(0, 0)).CategoryId);
            Assert.Equal(TerrainCategories.Floor, draft.Room.TerrainAt(new Cell(1, 1)).CategoryId);
            Assert.Equal(9, draft.Room.Width);
            Assert.Equal(7, draft.Room.Height);
        }

        [Fact]
        public void DirectionLetterBecomesOutwardOffset()
        {
            var doors = LegacyRoomAdapter.ToDraft(Warehouse()).Doors;
            Assert.Equal(Cell.Left, doors.Single(d => d.Id == "entry").Outward);
            Assert.Equal(Cell.Right, doors.Single(d => d.Id == "exit").Outward);
        }

        [Fact]
        public void NorthAndSouthDoorsMapToUpAndDown()
        {
            var data = Warehouse();
            data.Doors = new[] { new LegacyDoor("n", new Cell(4, 6), "N", "exit"), new LegacyDoor("s", new Cell(4, 0), "S", "entry") };
            data.RowsNorthToSouth = new[]
            {
                "____.____", "#.......#", "#.......#", "#.......#", "#.......#", "#.......#", "____.____",
            };
            var doors = LegacyRoomAdapter.ToDraft(data).Doors;
            Assert.Equal(Cell.Up, doors.Single(d => d.Id == "n").Outward);
            Assert.Equal(Cell.Down, doors.Single(d => d.Id == "s").Outward);
        }

        [Fact]
        public void ObjectsAndSpawnAreConverted()
        {
            var room = LegacyRoomAdapter.ToDraft(Warehouse()).Room;
            Assert.Equal(ObjectCategories.Box, room.ObjectAt(new Cell(4, 2)).CategoryId);
            Assert.Equal(ObjectCategories.Target, room.ObjectAt(new Cell(6, 2)).CategoryId);
            Assert.Equal(ObjectCategories.Spawn, room.ObjectAt(new Cell(2, 2)).CategoryId);
        }

        [Fact]
        public void WarehouseSampleHasNoBoundaryOrDoorIssues()
        {
            var issues = RoomValidator.ValidateDraft(LegacyRoomAdapter.ToDraft(Warehouse()), ValidationStage.Publish);
            Assert.DoesNotContain(issues, i => i.RuleId == "boundary.open" || i.RuleId == "door.geometry");
        }

        [Fact]
        public void UnknownDirectionIsRejected()
        {
            var data = Warehouse();
            data.Doors = new[] { new LegacyDoor("entry", new Cell(0, 3), "X", "entry") };
            Assert.Throws<System.FormatException>(() => LegacyRoomAdapter.ToDraft(data));
        }

        [Fact]
        public void DuplicateIdIsReportedAsFormatException()
        {
            var data = Warehouse();
            data.Boxes = new[] { new LegacyEntity("box-1", new Cell(4, 2)), new LegacyEntity("box-1", new Cell(3, 2)) };
            Assert.Throws<System.FormatException>(() => LegacyRoomAdapter.ToDraft(data));
        }

        [Fact]
        public void EmptyIdIsReportedAsFormatException()
        {
            var data = Warehouse();
            data.Targets = new[] { new LegacyEntity("", new Cell(6, 2)) };
            Assert.Throws<System.FormatException>(() => LegacyRoomAdapter.ToDraft(data));
        }

        [Fact]
        public void UnknownTerrainCharacterIsRejected()
        {
            var data = Warehouse();
            data.RowsNorthToSouth = Rows.Select((r, i) => i == 1 ? "#...?...#" : r).ToArray();
            Assert.Throws<System.FormatException>(() => LegacyRoomAdapter.ToDraft(data));
        }
    }
}
