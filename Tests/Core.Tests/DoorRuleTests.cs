using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class DoorRuleTests
    {
        static RoomDraftModel WithDoor(Cell door, Cell outward, bool openOutside = true)
        {
            var room = new Room("r", 5, 5);
            for (int x = 0; x < 5; x++)
                for (int y = 0; y < 5; y++)
                    room.SetTerrain(new Cell(x, y), x == 0 || y == 0 || x == 4 || y == 4 ? TerrainCategories.Wall : TerrainCategories.Floor);
            room.SetTerrain(door, TerrainCategories.Door, "d1");
            if (openOutside && room.Inside(door + outward)) room.SetTerrain(door + outward, TerrainCategories.Void);
            return new RoomDraftModel(room, new[] { new DoorInfo("d1", door, outward, "entry") }, "d1");
        }

        [Fact]
        public void WellFormedDoorHasNoIssue()
        {
            var draft = WithDoor(new Cell(0, 2), Cell.Left);
            Assert.DoesNotContain(RoomValidator.ValidateDraft(draft, ValidationStage.Publish), i => i.RuleId == "door.geometry");
        }

        [Fact]
        public void OutwardCellInsideTheRoomIsAnErrorInBothStages()
        {
            var draft = WithDoor(new Cell(0, 2), Cell.Right);
            foreach (var stage in new[] { ValidationStage.Draft, ValidationStage.Publish })
            {
                var issue = RoomValidator.ValidateDraft(draft, stage).Single(i => i.RuleId == "door.geometry");
                Assert.Equal(Severity.Error, issue.Severity);
                Assert.Equal("d1", issue.Location);
            }
        }

        [Fact]
        public void WallInsideTheDoorIsAnError()
        {
            var draft = WithDoor(new Cell(0, 2), Cell.Left);
            draft.Room.SetTerrain(new Cell(1, 2), TerrainCategories.Wall);
            Assert.Contains(RoomValidator.ValidateDraft(draft, ValidationStage.Draft), i => i.RuleId == "door.geometry" && i.Severity == Severity.Error);
        }

        [Fact]
        public void DoorRecordOnNonDoorTerrainIsAnError()
        {
            var draft = WithDoor(new Cell(0, 2), Cell.Left);
            draft.Room.SetTerrain(new Cell(0, 2), TerrainCategories.Wall);
            Assert.Contains(RoomValidator.ValidateDraft(draft, ValidationStage.Publish), i => i.RuleId == "door.geometry");
        }
    }
}
