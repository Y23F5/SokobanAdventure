using System;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class SpawnAtEntranceTests
    {
        static RoomDraftModel Draft(bool withEntrance = true)
        {
            var room = new Room("r", 7, 7);
            for (int x = 0; x < 7; x++)
                for (int y = 0; y < 7; y++)
                    room.SetTerrain(new Cell(x, y), x == 0 || y == 0 || x == 6 || y == 6 ? TerrainCategories.Wall : TerrainCategories.Floor);
            room.SetTerrain(new Cell(0, 3), TerrainCategories.Door, "entry");
            var doors = new[] { new DoorInfo("entry", new Cell(0, 3), Cell.Left, "entry") };
            return new RoomDraftModel(room, withEntrance ? doors : new DoorInfo[0], withEntrance ? "entry" : null);
        }

        [Fact]
        public void SpawnIsPlacedOnTheCellInsideThePrimaryEntrance()
        {
            var draft = Draft();
            new SetSpawnCommand(draft).Apply();
            Assert.Equal(new Cell(1, 3), draft.Room.ObjectsOf(ObjectCategories.Spawn).Single().Cell);
        }

        [Fact]
        public void SettingTwiceKeepsASingleSpawn()
        {
            var draft = Draft();
            new SetSpawnCommand(draft).Apply();
            new SetSpawnCommand(draft).Apply();
            Assert.Equal(new Cell(1, 3), draft.Room.ObjectsOf(ObjectCategories.Spawn).Single().Cell);
        }

        [Fact]
        public void RevertRemovesTheSpawn()
        {
            var draft = Draft();
            var cmd = new SetSpawnCommand(draft);
            cmd.Apply();
            cmd.Revert();
            Assert.Empty(draft.Room.ObjectsOf(ObjectCategories.Spawn));
        }

        [Fact]
        public void WithoutPrimaryEntranceIsRejected()
        {
            Assert.Throws<InvalidOperationException>(() => new SetSpawnCommand(Draft(false)).Apply());
        }

        [Fact]
        public void OtherObjectOnTheEntranceCellIsRejected()
        {
            var draft = Draft();
            draft.Room.AddObject("box-1", ObjectCategories.Box, new Cell(1, 3));
            Assert.Throws<InvalidOperationException>(() => new SetSpawnCommand(draft).Apply());
        }

        [Fact]
        public void SpawnOnTheEntranceCellDoesNotTriggerEntryOccupied()
        {
            var draft = Draft();
            new SetSpawnCommand(draft).Apply();
            foreach (var stage in new[] { ValidationStage.Draft, ValidationStage.Publish })
                Assert.DoesNotContain(RoomValidator.ValidateDraft(draft, stage), i => i.RuleId == "entry.occupied");
        }

        [Fact]
        public void BoxOnTheEntranceCellStillTriggersEntryOccupied()
        {
            var draft = Draft();
            draft.Room.AddObject("box-1", ObjectCategories.Box, new Cell(1, 3));
            Assert.Contains(RoomValidator.ValidateDraft(draft, ValidationStage.Publish), i => i.RuleId == "entry.occupied");
        }
    }
}
