using System.Collections.Generic;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class WorldPlacementTests
    {
        static WorldDocument World(params WorldRoomRef[] rooms) =>
            new WorldDocument("w", 1, new Cell(0, 0), new Cell(19, 9), rooms, new WorldConnection[0],
                new WorldEndpoint("a", "spawn"), new WorldEndpoint("a", "exit"), new[] { "a" });

        static WorldRoomRef Ref(string id, int x, int y) => new WorldRoomRef(id, id + ".json", new Cell(x, y), new string[0]);

        static readonly Dictionary<string, Cell> Sizes = new Dictionary<string, Cell>
        {
            ["a"] = new Cell(5, 5), ["b"] = new Cell(5, 5),
        };

        [Fact]
        public void FreeSpotInsideBoundsHasNoIssue()
        {
            var world = World(Ref("a", 0, 0));
            Assert.Null(WorldValidator.Placement(world, Sizes, "b", new Cell(6, 0), false, new Cell(5, 5)));
        }

        [Fact]
        public void OutsideBoundsIsReportedWithConflictCell()
        {
            var world = World(Ref("a", 0, 0));
            string issue = WorldValidator.Placement(world, Sizes, "b", new Cell(17, 0), false, new Cell(5, 5));
            Assert.Contains("超出世界边界", issue);
            Assert.Contains("(20, 0)", issue);
        }

        [Fact]
        public void NegativeOriginIsOutsideBounds()
        {
            var world = World(Ref("a", 0, 0));
            Assert.Contains("超出世界边界", WorldValidator.Placement(world, Sizes, "b", new Cell(-1, 0), false, new Cell(5, 5)));
        }

        [Fact]
        public void OverlapNamesBothRoomsAndTheFirstSharedCell()
        {
            var world = World(Ref("a", 0, 0));
            string issue = WorldValidator.Placement(world, Sizes, "b", new Cell(3, 2), false, new Cell(5, 5));
            Assert.Contains("b 与 a 占地重叠", issue);
            Assert.Contains("(3, 2)", issue);
        }

        [Fact]
        public void TouchingEdgesDoNotOverlap()
        {
            var world = World(Ref("a", 0, 0));
            Assert.Null(WorldValidator.Placement(world, Sizes, "b", new Cell(5, 0), false, new Cell(5, 5)));
        }

        [Fact]
        public void AlreadyPlacedIsReportedUnlessMoving()
        {
            var world = World(Ref("a", 0, 0));
            Assert.Equal("a 已放置", WorldValidator.Placement(world, Sizes, "a", new Cell(0, 0), false, new Cell(5, 5)));
            Assert.Null(WorldValidator.Placement(world, Sizes, "a", new Cell(0, 0), true, new Cell(5, 5)));
        }
    }
}
