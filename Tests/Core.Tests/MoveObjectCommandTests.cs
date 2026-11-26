using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class MoveObjectCommandTests
    {
        static RoomDraftModel Draft()
        {
            var room = new Room("r", 7, 7);
            for (int x = 0; x < 7; x++)
                for (int y = 0; y < 7; y++)
                    room.SetTerrain(new Cell(x, y), x == 0 || y == 0 || x == 6 || y == 6 ? TerrainCategories.Wall : TerrainCategories.Floor);
            room.SetTerrain(new Cell(0, 3), TerrainCategories.Door, "entry");
            var doors = new[] { new DoorInfo("entry", new Cell(0, 3), Cell.Left, "entry") };
            var draft = new RoomDraftModel(room, doors, "entry");
            room.AddObject("box-1", ObjectCategories.Box, new Cell(3, 3), new Cell(3, 3));
            room.AddObject("target-1", ObjectCategories.Target, new Cell(5, 5));
            return draft;
        }

        [Fact]
        public void MovesObjectToFreeFloorCell()
        {
            var draft = Draft();
            new MoveObjectCommand(draft, "box-1", new Cell(4, 4), false).Apply();
            Assert.NotNull(draft.Room.ObjectAt(new Cell(4, 4)));
            Assert.Null(draft.Room.ObjectAt(new Cell(3, 3)));
        }

        [Fact]
        public void KeepsRecoveryCellWhenNotSynced()
        {
            var draft = Draft();
            new MoveObjectCommand(draft, "box-1", new Cell(4, 4), false).Apply();
            Assert.Equal((Cell?)new Cell(3, 3), draft.Room.ObjectAt(new Cell(4, 4)).RecoveryCell);
        }

        [Fact]
        public void SyncsRecoveryCellForBoxWhenAsked()
        {
            var draft = Draft();
            new MoveObjectCommand(draft, "box-1", new Cell(4, 4), true).Apply();
            Assert.Equal((Cell?)new Cell(4, 4), draft.Room.ObjectAt(new Cell(4, 4)).RecoveryCell);
        }

        [Fact]
        public void SyncDoesNotGiveTargetARecoveryCell()
        {
            var draft = Draft();
            new MoveObjectCommand(draft, "target-1", new Cell(4, 5), true).Apply();
            Assert.Null(draft.Room.ObjectAt(new Cell(4, 5)).RecoveryCell);
        }

        [Fact]
        public void RevertRestoresCellAndRecoveryCell()
        {
            var draft = Draft();
            var cmd = new MoveObjectCommand(draft, "box-1", new Cell(4, 4), true);
            cmd.Apply();
            cmd.Revert();
            var box = draft.Room.ObjectAt(new Cell(3, 3));
            Assert.Equal("box-1", box.Id);
            Assert.Equal((Cell?)new Cell(3, 3), box.RecoveryCell);
            Assert.Null(draft.Room.ObjectAt(new Cell(4, 4)));
        }

        [Fact]
        public void RejectsWallDoorAndOccupiedCells()
        {
            var draft = Draft();
            Assert.Throws<System.InvalidOperationException>(() => new MoveObjectCommand(draft, "box-1", new Cell(0, 1), false).Apply());
            Assert.Throws<System.InvalidOperationException>(() => new MoveObjectCommand(draft, "box-1", new Cell(1, 3), false).Apply());
            Assert.Throws<System.InvalidOperationException>(() => new MoveObjectCommand(draft, "box-1", new Cell(5, 5), false).Apply());
        }

        [Fact]
        public void RejectsUnknownObject()
        {
            Assert.Throws<System.InvalidOperationException>(() => new MoveObjectCommand(Draft(), "nope", new Cell(4, 4), false).Apply());
        }

        [Fact]
        public void MovingToOwnCellIsAllowedAndKeepsObject()
        {
            var draft = Draft();
            new MoveObjectCommand(draft, "box-1", new Cell(3, 3), false).Apply();
            Assert.Equal("box-1", draft.Room.ObjectAt(new Cell(3, 3)).Id);
        }
    }
}
