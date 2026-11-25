using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class CellTests
    {
        [Fact]
        public void AddAndSubtractWorkPerComponent()
        {
            Assert.Equal(new Cell(3, 5), new Cell(1, 2) + new Cell(2, 3));
            Assert.Equal(new Cell(-1, -1), new Cell(1, 2) - new Cell(2, 3));
        }

        [Fact]
        public void SearchOrderIsUpRightDownLeft()
        {
            Assert.Equal(new[] { Cell.Up, Cell.Right, Cell.Down, Cell.Left }, Cell.SearchOrder);
        }

        [Fact]
        public void CellWorksAsDictionaryKey()
        {
            var map = new System.Collections.Generic.Dictionary<Cell, string> { [new Cell(1, 1)] = "a" };
            Assert.Equal("a", map[new Cell(1, 1)]);
        }
    }
}
