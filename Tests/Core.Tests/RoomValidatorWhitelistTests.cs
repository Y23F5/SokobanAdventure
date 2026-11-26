using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RoomValidatorWhitelistTests
    {
        static RoomDraftModel Draft()
        {
            var room = new Room("r", 5, 5);
            for (int x = 0; x < 5; x++)
                for (int y = 0; y < 5; y++)
                    room.SetTerrain(new Cell(x, y), x == 0 || y == 0 || x == 4 || y == 4 ? TerrainCategories.Wall : TerrainCategories.Floor);
            room.SetTerrain(new Cell(0, 2), TerrainCategories.Door, "d1");
            room.AddObject("spawn", ObjectCategories.Spawn, new Cell(2, 2));
            return new RoomDraftModel(room, new[] { new DoorInfo("d1", new Cell(0, 2), Cell.Left, "entry") }, "d1");
        }

        [Fact]
        public void ValidDraftHasNoLoadOrStructureErrors()
        {
            var draft = Draft();
            Assert.Empty(RoomValidator.LoadErrors(draft));
            Assert.Empty(RoomValidator.StructureErrors(draft));
        }

        [Fact]
        public void LoadErrorsReportSharedCell()
        {
            var draft = Draft();
            draft.Room.AddObject("box-1", ObjectCategories.Box, new Cell(3, 3));
            draft.Room.AddObject("box-2", ObjectCategories.Box, new Cell(3, 3));
            var issues = RoomValidator.LoadErrors(draft);
            Assert.Contains(issues, i => i.RuleId == "object.sharedCell" && i.Location == "box-2");
            Assert.All(issues, i => Assert.Equal(Severity.Error, i.Severity));
        }

        [Fact]
        public void LoadErrorsReportObjectNotOnFloor()
        {
            var draft = Draft();
            draft.Room.AddObject("box-1", ObjectCategories.Box, new Cell(1, 0));
            var issues = RoomValidator.LoadErrors(draft);
            Assert.Contains(issues, i => i.RuleId == "object.notOnFloor" && i.Location == "box-1");
            Assert.All(issues, i => Assert.Equal(Severity.Error, i.Severity));
        }

        [Fact]
        public void LoadErrorsOnlyContainTheTwoWhitelistedRules()
        {
            var draft = Draft();
            draft.Room.RemoveObject("spawn");
            draft.PrimaryEntranceId = "nope";
            draft.Room.AddObject("box-1", ObjectCategories.Box, new Cell(1, 0));
            draft.Room.AddObject("box-2", ObjectCategories.Box, new Cell(1, 0));
            draft.Room.SetTerrain(new Cell(4, 2), TerrainCategories.Floor);
            var ids = RoomValidator.LoadErrors(draft).Select(i => i.RuleId).Distinct().OrderBy(s => s).ToArray();
            Assert.Equal(new[] { "object.notOnFloor", "object.sharedCell" }, ids);
        }

        [Fact]
        public void LoadErrorsIgnoreStructureProblems()
        {
            var draft = Draft();
            draft.Room.RemoveObject("spawn");
            draft.PrimaryEntranceId = "nope";
            Assert.Empty(RoomValidator.LoadErrors(draft));
        }

        [Fact]
        public void StructureErrorsReportMissingSpawn()
        {
            var draft = Draft();
            draft.Room.RemoveObject("spawn");
            var issue = RoomValidator.StructureErrors(draft).Single(i => i.RuleId == "spawn.missing");
            Assert.Equal(Severity.Error, issue.Severity);
            Assert.Equal("r", issue.Location);
        }

        [Fact]
        public void StructureErrorsReportDoorGeometry()
        {
            var room = Draft().Room;
            var draft = new RoomDraftModel(room, new[] { new DoorInfo("d1", new Cell(0, 2), Cell.Right, "entry") }, "d1");
            var issue = RoomValidator.StructureErrors(draft).Single(i => i.RuleId == "door.geometry");
            Assert.Equal(Severity.Error, issue.Severity);
            Assert.Equal("d1", issue.Location);
        }

        [Fact]
        public void StructureErrorsReportOccupiedEntry()
        {
            var draft = Draft();
            draft.Room.AddObject("box-1", ObjectCategories.Box, new Cell(1, 2));
            var issue = RoomValidator.StructureErrors(draft).Single(i => i.RuleId == "entry.occupied");
            Assert.Equal(Severity.Error, issue.Severity);
            Assert.Equal("box-1", issue.Location);
        }

        [Fact]
        public void StructureErrorsReportMissingEntry()
        {
            var draft = Draft();
            draft.PrimaryEntranceId = "nope";
            var issue = RoomValidator.StructureErrors(draft).Single(i => i.RuleId == "entry.missing");
            Assert.Equal(Severity.Error, issue.Severity);
        }

        [Fact]
        public void StructureErrorsReportOpenBoundary()
        {
            var draft = Draft();
            draft.Room.SetTerrain(new Cell(4, 2), TerrainCategories.Floor);
            var issue = RoomValidator.StructureErrors(draft).Single(i => i.RuleId == "boundary.open");
            Assert.Equal(Severity.Error, issue.Severity);
            Assert.Equal(new Cell(4, 2).ToString(), issue.Location);
        }

        [Fact]
        public void StructureErrorsExcludeContentRules()
        {
            var draft = Draft();
            draft.Room.AddObject("t1", ObjectCategories.Target, new Cell(2, 3));
            draft.Room.AddObject("t2", ObjectCategories.Target, new Cell(3, 3));
            var issues = RoomValidator.StructureErrors(draft);
            Assert.DoesNotContain(issues, i => i.RuleId == "recovery.none" || i.RuleId == "target.none" || i.RuleId == "target.exceedsBoxes");
            Assert.Empty(issues);
        }

        [Fact]
        public void StructureErrorsExcludeTargetNone()
        {
            var draft = Draft();
            Assert.Empty(RoomValidator.StructureErrors(draft));
            Assert.Contains(RoomValidator.ValidateDraft(draft, ValidationStage.Publish), i => i.RuleId == "target.none");
        }

        [Fact]
        public void StructureErrorsExcludeLoadRules()
        {
            var draft = Draft();
            draft.Room.AddObject("box-1", ObjectCategories.Box, new Cell(3, 3));
            draft.Room.AddObject("box-2", ObjectCategories.Box, new Cell(3, 3));
            draft.Room.AddObject("box-3", ObjectCategories.Box, new Cell(1, 0));
            Assert.Empty(RoomValidator.StructureErrors(draft));
        }

        [Fact]
        public void StructureErrorsAreErrorsRegardlessOfStage()
        {
            var draft = Draft();
            draft.Room.RemoveObject("spawn");
            draft.PrimaryEntranceId = "nope";
            draft.Room.SetTerrain(new Cell(4, 2), TerrainCategories.Floor);
            var issues = RoomValidator.StructureErrors(draft);
            Assert.NotEmpty(issues);
            Assert.All(issues, i => Assert.Equal(Severity.Error, i.Severity));
            var ids = issues.Select(i => i.RuleId).Distinct().OrderBy(s => s).ToArray();
            Assert.Equal(new[] { "boundary.open", "entry.missing", "spawn.missing" }, ids);
        }
    }
}
