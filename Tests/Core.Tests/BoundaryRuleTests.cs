using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class BoundaryRuleTests
    {
        static RoomDraftModel Walled()
        {
            var room = new Room("r", 5, 5);
            for (int x = 0; x < 5; x++)
                for (int y = 0; y < 5; y++)
                    room.SetTerrain(new Cell(x, y), x == 0 || y == 0 || x == 4 || y == 4 ? TerrainCategories.Wall : TerrainCategories.Floor);
            return new RoomDraftModel(room);
        }

        [Fact]
        public void ClosedRoomHasNoBoundaryIssue()
        {
            Assert.DoesNotContain(RoomValidator.ValidateDraft(Walled(), ValidationStage.Publish), i => i.RuleId == "boundary.open");
        }

        [Fact]
        public void FloorNextToVoidIsHintInDraftAndErrorOnPublish()
        {
            var draft = Walled();
            draft.Room.SetTerrain(new Cell(0, 2), TerrainCategories.Void);
            draft.Room.SetTerrain(new Cell(1, 2), TerrainCategories.Floor);
            var d = RoomValidator.ValidateDraft(draft, ValidationStage.Draft).Where(i => i.RuleId == "boundary.open").ToList();
            var p = RoomValidator.ValidateDraft(draft, ValidationStage.Publish).Where(i => i.RuleId == "boundary.open").ToList();
            Assert.Single(d);
            Assert.Equal(Severity.Hint, d[0].Severity);
            Assert.Equal(Severity.Error, p.Single().Severity);
            Assert.Equal("(1,2)", p.Single().Location);
        }

        [Fact]
        public void FloorOnTheRoomEdgeCountsAsOpen()
        {
            var draft = Walled();
            draft.Room.SetTerrain(new Cell(4, 2), TerrainCategories.Floor);
            Assert.Contains(RoomValidator.ValidateDraft(draft, ValidationStage.Publish), i => i.RuleId == "boundary.open" && i.Location == "(4,2)");
        }

        [Fact]
        public void DoorCellIsExemptFromTheBoundaryRule()
        {
            var draft = Walled();
            draft.Room.SetTerrain(new Cell(0, 2), TerrainCategories.Door, "entry");
            var doors = new[] { new DoorInfo("entry", new Cell(0, 2), Cell.Left, "entry") };
            var withDoor = new RoomDraftModel(draft.Room, doors, "entry");
            Assert.DoesNotContain(RoomValidator.ValidateDraft(withDoor, ValidationStage.Publish), i => i.RuleId == "boundary.open");
        }
    }
}
