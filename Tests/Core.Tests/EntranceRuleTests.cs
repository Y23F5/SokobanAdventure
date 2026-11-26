using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class EntranceRuleTests
    {
        static RoomDraftModel Draft(string primaryId, string role = "entry")
        {
            var room = new Room("r", 5, 5);
            for (int x = 0; x < 5; x++)
                for (int y = 0; y < 5; y++)
                    room.SetTerrain(new Cell(x, y), x == 0 || y == 0 || x == 4 || y == 4 ? TerrainCategories.Wall : TerrainCategories.Floor);
            room.SetTerrain(new Cell(0, 2), TerrainCategories.Door, "d1");
            return new RoomDraftModel(room, new[] { new DoorInfo("d1", new Cell(0, 2), Cell.Left, role) }, primaryId);
        }

        [Fact]
        public void ExistingEntryDoorHasNoEntranceIssue()
        {
            var issues = RoomValidator.ValidateDraft(Draft("d1"), ValidationStage.Publish);
            Assert.DoesNotContain(issues, i => i.RuleId == "entry.missing" || i.RuleId == "entry.occupied");
        }

        [Fact]
        public void UnknownPrimaryEntranceIsHintInDraftAndErrorInPublish()
        {
            var draftIssue = RoomValidator.ValidateDraft(Draft("nope"), ValidationStage.Draft).Single(i => i.RuleId == "entry.missing");
            var publishIssue = RoomValidator.ValidateDraft(Draft("nope"), ValidationStage.Publish).Single(i => i.RuleId == "entry.missing");
            Assert.Equal(Severity.Hint, draftIssue.Severity);
            Assert.Equal(Severity.Error, publishIssue.Severity);
            Assert.Equal("r", publishIssue.Location);
        }

        [Fact]
        public void PrimaryEntranceWithExitRoleIsMissing()
        {
            var issues = RoomValidator.ValidateDraft(Draft("d1", "exit"), ValidationStage.Publish);
            Assert.Contains(issues, i => i.RuleId == "entry.missing");
        }

        [Fact]
        public void ObjectOnTheCellInsideThePrimaryEntranceIsAnErrorInBothStages()
        {
            var draft = Draft("d1");
            draft.Room.AddObject("box-1", ObjectCategories.Box, new Cell(1, 2));
            foreach (var stage in new[] { ValidationStage.Draft, ValidationStage.Publish })
            {
                var issue = RoomValidator.ValidateDraft(draft, stage).Single(i => i.RuleId == "entry.occupied");
                Assert.Equal(Severity.Error, issue.Severity);
                Assert.Equal("box-1", issue.Location);
            }
        }

        [Fact]
        public void ObjectElsewhereDoesNotBlockTheEntrance()
        {
            var draft = Draft("d1");
            draft.Room.AddObject("box-1", ObjectCategories.Box, new Cell(2, 2));
            Assert.DoesNotContain(RoomValidator.ValidateDraft(draft, ValidationStage.Publish), i => i.RuleId == "entry.occupied");
        }
    }
}
