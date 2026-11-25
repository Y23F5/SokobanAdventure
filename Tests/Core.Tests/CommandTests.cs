using System;
using System.Collections.Generic;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class CommandTests
    {
        sealed class Counter : ICommand
        {
            public int Value;
            public void Apply() => Value++;
            public void Revert() => Value--;
        }

        sealed class Failing : ICommand
        {
            public void Apply() => throw new InvalidOperationException("bad");
            public void Revert() { }
        }

        [Fact]
        public void ExecuteUndoRedoRoundTrip()
        {
            var h = new CommandHistory();
            var c = new Counter();
            h.Execute(c);
            Assert.Equal(1, c.Value);
            Assert.True(h.Undo());
            Assert.Equal(0, c.Value);
            Assert.True(h.Redo());
            Assert.Equal(1, c.Value);
            Assert.False(h.Redo());
        }

        [Fact]
        public void DirtyFollowsRevisionAgainstSavedBaseline()
        {
            var h = new CommandHistory();
            Assert.False(h.Dirty);
            h.Execute(new Counter());
            Assert.True(h.Dirty);
            h.MarkSaved();
            Assert.False(h.Dirty);
            h.Execute(new Counter());
            Assert.True(h.Dirty);
            h.Undo();
            Assert.False(h.Dirty);
            h.Undo();
            Assert.True(h.Dirty);
            h.Redo();
            Assert.False(h.Dirty);
        }

        [Fact]
        public void NewCommandAfterUndoClearsRedoAndNeverFalselyMatchesSave()
        {
            var h = new CommandHistory();
            h.Execute(new Counter());
            h.MarkSaved();
            h.Undo();
            h.Execute(new Counter());
            Assert.True(h.Dirty);
            Assert.False(h.CanRedo);
        }

        [Fact]
        public void FailedCommandIsNotRecorded()
        {
            var h = new CommandHistory();
            Assert.Throws<InvalidOperationException>(() => h.Execute(new Failing()));
            Assert.False(h.CanUndo);
            Assert.Equal(0, h.Revision);
            Assert.False(h.Dirty);
        }

        static Room FloorRoom()
        {
            var room = new Room("r", 4, 4);
            for (int x = 0; x < 4; x++)
                for (int y = 0; y < 4; y++)
                    room.SetTerrain(new Cell(x, y), TerrainCategories.Floor);
            return room;
        }

        [Fact]
        public void StrokeAppliesAndRevertsWholeStroke()
        {
            var room = FloorRoom();
            var h = new CommandHistory();
            h.Execute(new PaintStroke(room, new[] { new Cell(0, 0), new Cell(1, 0), new Cell(2, 0) }, TerrainCategories.Wall));
            Assert.Equal(TerrainCategories.Wall, room.TerrainAt(new Cell(1, 0)).CategoryId);
            h.Undo();
            Assert.Equal(TerrainCategories.Floor, room.TerrainAt(new Cell(1, 0)).CategoryId);
            Assert.Equal(TerrainCategories.Floor, room.TerrainAt(new Cell(2, 0)).CategoryId);
        }

        [Fact]
        public void StrokeWithOneIllegalCellIsRejectedWhole()
        {
            var room = FloorRoom();
            var h = new CommandHistory();
            var stroke = new PaintStroke(room, new[] { new Cell(0, 0), new Cell(9, 9) }, TerrainCategories.Wall);
            Assert.Throws<InvalidOperationException>(() => h.Execute(stroke));
            Assert.Equal(TerrainCategories.Floor, room.TerrainAt(new Cell(0, 0)).CategoryId);
            Assert.False(h.CanUndo);
            Assert.False(h.Dirty);
        }

        [Fact]
        public void StrokeOverObjectIsRejectedWhenNotFloor()
        {
            var room = FloorRoom();
            room.AddObject("box-1", ObjectCategories.Box, new Cell(1, 1));
            var stroke = new PaintStroke(room, new[] { new Cell(1, 1) }, TerrainCategories.Wall);
            Assert.NotNull(stroke.Precheck());
        }

        [Fact]
        public void RevertRestoresTerrainEntityIds()
        {
            var room = FloorRoom();
            room.SetTerrain(new Cell(0, 0), TerrainCategories.Wall, "wall-a");
            var h = new CommandHistory();
            h.Execute(new PaintStroke(room, new[] { new Cell(0, 0) }, TerrainCategories.Floor));
            Assert.False(room.Ids.Contains("wall-a"));
            h.Undo();
            Assert.True(room.Ids.Contains("wall-a"));
            Assert.Equal(TerrainCategories.Wall, room.TerrainAt(new Cell(0, 0)).CategoryId);
        }
    }
}
