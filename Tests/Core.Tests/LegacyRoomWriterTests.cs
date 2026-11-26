using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class LegacyRoomWriterTests
    {
        static LegacyRoomData Sample() => new LegacyRoomData
        {
            LevelId = "warehouse",
            Width = 9,
            Height = 7,
            RowsNorthToSouth = new[]
            {
                "#########", "#.......#", "#.......#", ".........", "#.......#", "#.......#", "#########",
            },
            Doors = new[]
            {
                new LegacyDoor("entry", new Cell(0, 3), "W", "entry"),
                new LegacyDoor("exit", new Cell(8, 3), "E", "exit"),
            },
            PrimaryEntranceId = "entry",
            Boxes = new[] { new LegacyEntity("box-1", new Cell(4, 2), new Cell(4, 2)) },
            Targets = new[] { new LegacyEntity("target-1", new Cell(6, 2)) },
            Spawn = new LegacyEntity("spawn", new Cell(2, 2)),
        };

        static void AssertSame(LegacyRoomData a, LegacyRoomData b)
        {
            Assert.Equal(a.LevelId, b.LevelId);
            Assert.Equal(a.Width, b.Width);
            Assert.Equal(a.Height, b.Height);
            Assert.Equal(a.RowsNorthToSouth, b.RowsNorthToSouth);
            Assert.Equal(a.PrimaryEntranceId, b.PrimaryEntranceId);
            Assert.Equal(a.Doors.Select(d => (d.Id, d.Cell, d.Direction, d.Role)).OrderBy(d => d.Id), b.Doors.Select(d => (d.Id, d.Cell, d.Direction, d.Role)).OrderBy(d => d.Id));
            Assert.Equal(a.Boxes.Select(e => (e.Id, e.Cell, e.RecoveryCell)).OrderBy(e => e.Id), b.Boxes.Select(e => (e.Id, e.Cell, e.RecoveryCell)).OrderBy(e => e.Id));
            Assert.Equal(a.Targets.Select(e => (e.Id, e.Cell)).OrderBy(e => e.Id), b.Targets.Select(e => (e.Id, e.Cell)).OrderBy(e => e.Id));
            Assert.Equal((a.Spawn.Id, a.Spawn.Cell), (b.Spawn.Id, b.Spawn.Cell));
        }

        [Fact]
        public void RoundTripThroughDraftIsIdentity()
        {
            var data = Sample();
            var back = LegacyRoomWriter.FromDraft(LegacyRoomAdapter.ToDraft(data));
            AssertSame(data, back);
        }

        [Fact]
        public void EditedDraftIsReflectedInOutput()
        {
            var draft = LegacyRoomAdapter.ToDraft(Sample());
            draft.Room.AddObject("box-2", ObjectCategories.Box, new Cell(3, 4), new Cell(3, 4));
            var back = LegacyRoomWriter.FromDraft(draft);
            Assert.Equal(2, back.Boxes.Length);
            Assert.Equal((Cell?)new Cell(3, 4), back.Boxes.Single(b => b.Id == "box-2").RecoveryCell);
        }

        [Fact]
        public void DoorOutwardBecomesDirectionLetter()
        {
            var back = LegacyRoomWriter.FromDraft(LegacyRoomAdapter.ToDraft(Sample()));
            Assert.Equal("W", back.Doors.Single(d => d.Id == "entry").Direction);
            Assert.Equal("E", back.Doors.Single(d => d.Id == "exit").Direction);
        }

        [Fact]
        public void MissingSpawnGivesNullSpawn()
        {
            var draft = LegacyRoomAdapter.ToDraft(Sample());
            draft.Room.RemoveObject("spawn");
            Assert.Null(LegacyRoomWriter.FromDraft(draft).Spawn);
        }
    }
}
