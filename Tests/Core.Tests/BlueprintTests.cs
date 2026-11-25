using System;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class BlueprintTests
    {
        static readonly string[] Warehouse =
        {
            "#########",
            "#.......#",
            "#.......#",
            "<.......>",
            "#.P.B.T.#",
            "#.......#",
            "#########",
        };

        static readonly string[] Forest =
        {
            "#############",
            "#...######T##",
            "#...V.B.....#",
            "<...#..##...#",
            "#.P.#..##...>",
            "#####..B....#",
            "##########T##",
            "#############",
        };

        [Fact]
        public void WarehouseParsesWithOriginAtBottomLeft()
        {
            var r = Blueprint.Parse("warehouse", Warehouse);
            Assert.Equal(9, r.Room.Width);
            Assert.Equal(7, r.Room.Height);
            Assert.Equal(new Cell(2, 2), r.Room.ObjectsOf(ObjectCategories.Spawn).Single().Cell);
            Assert.Equal(new Cell(4, 2), r.Room.ObjectsOf(ObjectCategories.Box).Single().Cell);
            Assert.Equal(new Cell(6, 2), r.Room.ObjectsOf(ObjectCategories.Target).Single().Cell);
        }

        [Fact]
        public void DoorsGetDirectionAndRoleFromPosition()
        {
            var r = Blueprint.Parse("warehouse", Warehouse);
            var entry = r.Doors.Single(d => d.Role == "entry");
            var exit = r.Doors.Single(d => d.Role == "exit");
            Assert.Equal(new Cell(0, 3), entry.Cell);
            Assert.Equal(Cell.Left, entry.Outward);
            Assert.Equal(new Cell(8, 3), exit.Cell);
            Assert.Equal(Cell.Right, exit.Outward);
        }

        [Fact]
        public void EntryBufferIsInnerCellOfPrimaryEntrance()
        {
            var r = Blueprint.Parse("warehouse", Warehouse);
            Assert.Equal(new Cell(1, 3), r.EntryBuffer);
        }

        [Fact]
        public void WarehouseHasNoPublishErrors()
        {
            var r = Blueprint.Parse("warehouse", Warehouse);
            var issues = RoomValidator.Validate(r.Room, ValidationStage.Publish, r.EntryBuffer);
            Assert.DoesNotContain(issues, i => i.Severity == Severity.Error);
        }

        [Fact]
        public void ForestKeepsVineGateAndTwoTargets()
        {
            var r = Blueprint.Parse("forest", Forest);
            Assert.Equal(13, r.Room.Width);
            Assert.Equal(8, r.Room.Height);
            Assert.Equal(TerrainCategories.Openable, r.Room.TerrainAt(new Cell(4, 5)).CategoryId);
            Assert.Equal(2, r.Room.ObjectsOf(ObjectCategories.Target).Count());
            Assert.Equal(2, r.Room.ObjectsOf(ObjectCategories.Box).Count());
        }

        [Fact]
        public void UnknownMarkIsRejected()
        {
            Assert.Throws<FormatException>(() => Blueprint.Parse("x", new[] { "#?#" }));
        }

        [Fact]
        public void RaggedRowsAreRejected()
        {
            Assert.Throws<FormatException>(() => Blueprint.Parse("x", new[] { "###", "##" }));
        }

        [Fact]
        public void DoorNotOnEdgeIsRejected()
        {
            Assert.Throws<FormatException>(() => Blueprint.Parse("x", new[] { "###", "#<#", "###" }));
        }
    }
}
