using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RecoveryRulesTests
    {
        static bool Open(Cell c) => c.X >= 0 && c.Y >= 0 && c.X < 6 && c.Y < 6;

        [Fact]
        public void RouteGoesAlongXThenY()
        {
            var route = RecoveryRules.Route(new Cell(1, 1), new Cell(3, 3));
            Assert.Equal(new[] { new Cell(1, 1), new Cell(2, 1), new Cell(3, 1), new Cell(3, 2), new Cell(3, 3) }, route);
        }

        [Fact]
        public void RouteOfSameCellIsSingle()
        {
            Assert.Single(RecoveryRules.Route(new Cell(2, 2), new Cell(2, 2)));
        }

        [Fact]
        public void ZoneIsUnionOfBoxRoutes()
        {
            var zone = RecoveryRules.OccupiedZone(new[]
            {
                new BoxMove(new Cell(4, 1), new Cell(2, 1)),
                new BoxMove(new Cell(2, 1), new Cell(2, 3)),
            });
            Assert.Equal(5, zone.Count);
            Assert.Contains(new Cell(3, 1), zone);
            Assert.Contains(new Cell(2, 2), zone);
        }

        [Fact]
        public void PlayerOutsideForbiddenStaysPut()
        {
            var spot = RecoveryRules.SafeCell(new Cell(0, 0), Open, new HashSet<Cell>(), new HashSet<Cell>(), null);
            Assert.Equal(new Cell(0, 0), spot);
        }

        [Fact]
        public void SearchOrderIsUpRightDownLeft()
        {
            var forbidden = new HashSet<Cell> { new Cell(2, 2) };
            var spot = RecoveryRules.SafeCell(new Cell(2, 2), Open, forbidden, new HashSet<Cell>(), null);
            Assert.Equal(new Cell(2, 3), spot);
        }

        [Fact]
        public void BoxCellsAreSkipped()
        {
            var forbidden = new HashSet<Cell> { new Cell(2, 2) };
            var boxes = new HashSet<Cell> { new Cell(2, 3) };
            var spot = RecoveryRules.SafeCell(new Cell(2, 2), Open, forbidden, boxes, null);
            Assert.Equal(new Cell(3, 2), spot);
        }

        [Fact]
        public void FallsBackToEntryBufferWhenNothingReachable()
        {
            bool Pocket(Cell c) => c == new Cell(0, 0) || c == new Cell(5, 5);
            var forbidden = new HashSet<Cell> { new Cell(0, 0) };
            var spot = RecoveryRules.SafeCell(new Cell(0, 0), Pocket, forbidden, new HashSet<Cell>(), new Cell(5, 5));
            Assert.Equal(new Cell(5, 5), spot);
        }

        [Fact]
        public void OccupiedEntryBufferUsesNearestLegalCell()
        {
            bool Pocket(Cell c) => c == new Cell(0, 0) || (c.Y == 5 && c.X >= 3 && c.X <= 5);
            var forbidden = new HashSet<Cell> { new Cell(0, 0) };
            var boxes = new HashSet<Cell> { new Cell(3, 5) };
            var spot = RecoveryRules.SafeCell(new Cell(0, 0), Pocket, forbidden, boxes, new Cell(3, 5));
            Assert.Equal(new Cell(4, 5), spot);
        }

        [Fact]
        public void SameInputGivesSameResult()
        {
            var forbidden = new HashSet<Cell> { new Cell(2, 2), new Cell(2, 3) };
            var a = RecoveryRules.SafeCell(new Cell(2, 2), Open, forbidden, new HashSet<Cell>(), null);
            var b = RecoveryRules.SafeCell(new Cell(2, 2), Open, forbidden, new HashSet<Cell>(), null);
            Assert.Equal(a, b);
        }
    }
}
