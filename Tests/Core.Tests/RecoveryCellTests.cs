using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RecoveryCellTests
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
        public void AddObjectKeepsRecoveryCell()
        {
            var placed = Base().AddObject("box-1", ObjectCategories.Box, new Cell(2, 2), new Cell(3, 3));
            Assert.Equal((Cell?)new Cell(3, 3), placed.RecoveryCell);
        }

        [Fact]
        public void RecoveryCellDefaultsToNull()
        {
            var placed = Base().AddObject("box-1", ObjectCategories.Box, new Cell(2, 2));
            Assert.Null(placed.RecoveryCell);
        }

        [Fact]
        public void CodecRoundTripKeepsRecoveryCellAndNull()
        {
            var room = Base();
            room.AddObject("box-1", ObjectCategories.Box, new Cell(2, 2), new Cell(3, 3));
            room.AddObject("box-2", ObjectCategories.Box, new Cell(2, 3));
            var back = RoomCodec.Decode(RoomCodec.Encode(new RoomDocument(room, null, null, null))).Room;
            Assert.Equal((Cell?)new Cell(3, 3), back.ObjectAt(new Cell(2, 2)).RecoveryCell);
            Assert.Null(back.ObjectAt(new Cell(2, 3)).RecoveryCell);
        }

        [Fact]
        public void DuplicateRecoveryCellIsErrorInBothStages()
        {
            var room = Base();
            room.AddObject("box-1", ObjectCategories.Box, new Cell(2, 2), new Cell(3, 3));
            room.AddObject("box-2", ObjectCategories.Box, new Cell(2, 3), new Cell(3, 3));
            foreach (var stage in new[] { ValidationStage.Draft, ValidationStage.Publish })
            {
                var issue = RoomValidator.Validate(room, stage, null).Single(i => i.RuleId == "box.recoveryDuplicate");
                Assert.Equal(Severity.Error, issue.Severity);
                Assert.Equal("box-2", issue.Location);
            }
        }

        [Fact]
        public void RecoveryCellOffFloorIsErrorInBothStages()
        {
            var room = Base();
            room.AddObject("box-1", ObjectCategories.Box, new Cell(2, 2), new Cell(0, 0));
            foreach (var stage in new[] { ValidationStage.Draft, ValidationStage.Publish })
            {
                var issue = RoomValidator.Validate(room, stage, null).Single(i => i.RuleId == "box.recoveryNotFloor");
                Assert.Equal(Severity.Error, issue.Severity);
                Assert.Equal("box-1", issue.Location);
            }
        }

        [Fact]
        public void ValidRecoveryCellsRaiseNoRecoveryIssue()
        {
            var room = Base();
            room.AddObject("box-1", ObjectCategories.Box, new Cell(2, 2), new Cell(3, 3));
            room.AddObject("box-2", ObjectCategories.Box, new Cell(2, 3), new Cell(4, 4));
            Assert.DoesNotContain(RoomValidator.Validate(room, ValidationStage.Publish, null), i => i.RuleId.StartsWith("box.recovery"));
        }
    }
}
