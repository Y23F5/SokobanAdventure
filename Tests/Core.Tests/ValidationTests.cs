using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class ValidationTests
    {
        static Room FloorRoom()
        {
            var room = new Room("r", 5, 5);
            for (int x = 0; x < 5; x++)
                for (int y = 0; y < 5; y++)
                    room.SetTerrain(new Cell(x, y), TerrainCategories.Floor);
            return room;
        }

        static Room CompleteRoom()
        {
            var room = FloorRoom();
            room.AddObject("spawn", ObjectCategories.Spawn, new Cell(0, 0));
            room.AddObject("box-1", ObjectCategories.Box, new Cell(2, 2));
            room.AddObject("target-1", ObjectCategories.Target, new Cell(3, 2));
            return room;
        }

        [Fact]
        public void CompleteRoomHasNoIssues()
        {
            Assert.Empty(RoomValidator.Validate(CompleteRoom(), ValidationStage.Publish, new Cell(1, 0)));
        }

        [Fact]
        public void MissingSpawnIsHintInDraftAndErrorInPublish()
        {
            var room = FloorRoom();
            var draft = RoomValidator.Validate(room, ValidationStage.Draft, new Cell(1, 0));
            var publish = RoomValidator.Validate(room, ValidationStage.Publish, new Cell(1, 0));
            Assert.Equal(Severity.Hint, draft.First(i => i.RuleId == "spawn.missing").Severity);
            Assert.Equal(Severity.Error, publish.First(i => i.RuleId == "spawn.missing").Severity);
            Assert.False(RoomValidator.HasErrors(draft.Where(i => i.RuleId == "spawn.missing")));
            Assert.True(RoomValidator.HasErrors(publish));
        }

        [Fact]
        public void SameRuleSetRunsInBothStages()
        {
            var room = FloorRoom();
            var draft = RoomValidator.Validate(room, ValidationStage.Draft, null).Select(i => i.RuleId);
            var publish = RoomValidator.Validate(room, ValidationStage.Publish, null).Select(i => i.RuleId);
            Assert.Equal(draft, publish);
        }

        [Fact]
        public void ObjectOnWallIsAnErrorInBothStages()
        {
            var room = FloorRoom();
            room.SetTerrain(new Cell(1, 1), TerrainCategories.Wall);
            room.AddObject("box-1", ObjectCategories.Box, new Cell(1, 1));
            foreach (var stage in new[] { ValidationStage.Draft, ValidationStage.Publish })
                Assert.Contains(RoomValidator.Validate(room, stage, null),
                    i => i.RuleId == "object.notOnFloor" && i.Severity == Severity.Error);
        }

        [Fact]
        public void SharedCellIsReported()
        {
            var room = FloorRoom();
            room.AddObject("box-1", ObjectCategories.Box, new Cell(2, 2));
            room.AddObject("target-1", ObjectCategories.Target, new Cell(2, 2));
            Assert.Contains(RoomValidator.Validate(room, ValidationStage.Draft, null),
                i => i.RuleId == "object.sharedCell" && i.Location == "target-1");
        }

        [Fact]
        public void EntryBufferCellMustStayEmpty()
        {
            var room = CompleteRoom();
            room.AddObject("box-2", ObjectCategories.Box, new Cell(1, 0));
            Assert.Contains(RoomValidator.Validate(room, ValidationStage.Publish, new Cell(1, 0)),
                i => i.RuleId == "entry.occupied" && i.Location == "box-2");
        }

        [Fact]
        public void MoreTargetsThanBoxesFailsPublishOnly()
        {
            var room = CompleteRoom();
            room.AddObject("target-2", ObjectCategories.Target, new Cell(4, 4));
            var draft = RoomValidator.Validate(room, ValidationStage.Draft, new Cell(1, 0));
            var publish = RoomValidator.Validate(room, ValidationStage.Publish, new Cell(1, 0));
            Assert.Equal(Severity.Hint, draft.Single(i => i.RuleId == "target.exceedsBoxes").Severity);
            Assert.Equal(Severity.Error, publish.Single(i => i.RuleId == "target.exceedsBoxes").Severity);
        }

        [Fact]
        public void SecondSpawnIsErrorEvenInDraft()
        {
            var room = CompleteRoom();
            room.AddObject("spawn-2", ObjectCategories.Spawn, new Cell(4, 4));
            Assert.Contains(RoomValidator.Validate(room, ValidationStage.Draft, new Cell(1, 0)),
                i => i.RuleId == "spawn.multiple" && i.Severity == Severity.Error);
        }
    }
}
