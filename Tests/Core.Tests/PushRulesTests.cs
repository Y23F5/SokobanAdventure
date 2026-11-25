using System.Collections.Generic;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class PushRulesTests
    {
        static Room MakeRoom()
        {
            var room = new Room("r", 7, 3);
            for (int x = 0; x < 7; x++)
                for (int y = 0; y < 3; y++)
                    room.SetTerrain(new Cell(x, y), TerrainCategories.Floor);
            room.SetTerrain(new Cell(5, 1), TerrainCategories.Wall);
            room.SetTerrain(new Cell(4, 0), TerrainCategories.Openable, "vine");
            room.AddObject("spawn", ObjectCategories.Spawn, new Cell(0, 1));
            room.AddObject("box-1", ObjectCategories.Box, new Cell(2, 1));
            room.AddObject("box-2", ObjectCategories.Box, new Cell(3, 2));
            room.AddObject("target-1", ObjectCategories.Target, new Cell(3, 1));
            return room;
        }

        static RecoverySession Session(Room room) =>
            new RecoverySession(room, new Dictionary<string, Cell>
            {
                ["box-1"] = new Cell(2, 1),
                ["box-2"] = new Cell(3, 2),
            }, new Cell(0, 1), new Cell(1, 1));

        [Fact]
        public void WalkingIntoEmptyCellMoves()
        {
            var s = Session(MakeRoom());
            var r = PushRules.TryPush(s, new Cell(0, 1), Cell.Right);
            Assert.True(r.Moved);
            Assert.False(r.Pushed);
            Assert.Equal(new Cell(1, 1), r.Player);
        }

        [Fact]
        public void PushingBoxOntoTargetSolvesRoom()
        {
            var room = MakeRoom();
            var s = Session(room);
            var r = PushRules.TryPush(s, new Cell(1, 1), Cell.Right);
            Assert.True(r.Pushed);
            Assert.Equal("box-1", r.PushedBoxId);
            Assert.Equal(new Cell(3, 1), s.Boxes["box-1"]);
            Assert.True(PushRules.Solved(room, s.Boxes));
        }

        [Fact]
        public void BoxCannotBePushedIntoWallOrAnotherBox()
        {
            var room = MakeRoom();
            var s = Session(room);
            s.Boxes["box-1"] = new Cell(4, 1);
            var blocked = PushRules.TryPush(s, new Cell(3, 1), Cell.Right);
            Assert.False(blocked.Moved);
            Assert.Equal(new Cell(4, 1), s.Boxes["box-1"]);

            s.Boxes["box-1"] = new Cell(3, 1);
            s.Boxes["box-2"] = new Cell(4, 1);
            var twoBoxes = PushRules.TryPush(s, new Cell(2, 1), Cell.Right);
            Assert.False(twoBoxes.Moved);
        }

        [Fact]
        public void BoxCannotBePushedIntoClosedOpenable()
        {
            var room = MakeRoom();
            var s = Session(room);
            s.Boxes["box-2"] = new Cell(3, 0);
            var r = PushRules.TryPush(s, new Cell(2, 0), Cell.Right);
            Assert.False(r.Moved);
            Assert.Equal(new Cell(3, 0), s.Boxes["box-2"]);
        }

        [Fact]
        public void NoPushWhileRecovering()
        {
            var room = MakeRoom();
            var s = Session(room);
            s.Start(3f);
            var r = PushRules.TryPush(s, new Cell(1, 1), Cell.Right);
            Assert.False(r.Pushed);
        }

        [Fact]
        public void SolvedNeedsEveryTargetCoveredAndAtLeastOneTarget()
        {
            var room = MakeRoom();
            Assert.False(PushRules.Solved(room, new Dictionary<string, Cell> { ["box-1"] = new Cell(2, 1) }));
            Assert.True(PushRules.Solved(room, new Dictionary<string, Cell>
                { ["box-1"] = new Cell(3, 1), ["box-2"] = new Cell(0, 0) }));
            var noTargets = new Room("n", 2, 2);
            Assert.False(PushRules.Solved(noTargets, new Dictionary<string, Cell> { ["b"] = new Cell(0, 0) }));
        }

        [Fact]
        public void WallBlocksWalking()
        {
            var s = Session(MakeRoom());
            var r = PushRules.TryPush(s, new Cell(4, 1), Cell.Right);
            Assert.False(r.Moved);
        }
    }
}
