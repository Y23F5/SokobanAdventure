using System;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class ExpandTests
    {
        static RoomHolder Make()
        {
            var room = new Room("r", 3, 3);
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 3; y++)
                    room.SetTerrain(new Cell(x, y), TerrainCategories.Floor);
            room.SetTerrain(new Cell(0, 0), TerrainCategories.Wall, "wall-a");
            room.AddObject("box-1", ObjectCategories.Box, new Cell(1, 1));
            return new RoomHolder(room);
        }

        [Fact]
        public void ExpandWestAndSouthShiftsEverything()
        {
            var h = Make();
            var history = new CommandHistory();
            history.Execute(new ExpandCommand(h, 2, 1, 0, 0));
            Assert.Equal(5, h.Room.Width);
            Assert.Equal(4, h.Room.Height);
            Assert.Equal(new Cell(3, 2), h.Room.ObjectAt(new Cell(3, 2)).Cell);
            Assert.Equal("box-1", h.Room.ObjectAt(new Cell(3, 2)).Id);
            Assert.Equal("wall-a", h.Room.TerrainAt(new Cell(2, 1)).EntityId);
            Assert.Equal(TerrainCategories.Void, h.Room.TerrainAt(new Cell(0, 0)).CategoryId);
        }

        [Fact]
        public void UndoRestoresOldRoomExactly()
        {
            var h = Make();
            var history = new CommandHistory();
            history.Execute(new ExpandCommand(h, 2, 1, 1, 1));
            history.Undo();
            Assert.Equal(3, h.Room.Width);
            Assert.Equal(new Cell(1, 1), h.Room.ObjectAt(new Cell(1, 1)).Cell);
            Assert.True(h.Room.Ids.Contains("wall-a"));
            history.Redo();
            Assert.Equal(6, h.Room.Width);
        }

        [Fact]
        public void NegativeOrZeroExpandIsRejectedWithoutHistory()
        {
            var h = Make();
            var history = new CommandHistory();
            Assert.Throws<InvalidOperationException>(() => history.Execute(new ExpandCommand(h, -1, 0, 0, 0)));
            Assert.Throws<InvalidOperationException>(() => history.Execute(new ExpandCommand(h, 0, 0, 0, 0)));
            Assert.False(history.CanUndo);
            Assert.False(history.Dirty);
        }

        [Fact]
        public void IdsSurviveExpansion()
        {
            var h = Make();
            new ExpandCommand(h, 1, 1, 1, 1).Apply();
            Assert.True(h.Room.Ids.Contains("box-1"));
            Assert.True(h.Room.Ids.Contains("wall-a"));
        }
    }
}
