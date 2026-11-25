using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class SolverTests
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

        static readonly string[] Factory =
        {
            "###########",
            "#....######",
            "#....B.TT##",
            "<......##.>",
            "#...B.###.#",
            "#.P.......#",
            "###########",
        };

        static Cell Spawn(Room r) => r.ObjectsOf(ObjectCategories.Spawn).Single().Cell;

        [Fact]
        public void WarehouseIsSolvedAndReplayConfirms()
        {
            var room = Blueprint.Parse("warehouse", Warehouse).Room;
            var result = Solver.Solve(room, Spawn(room), 100000);
            Assert.Equal(SolveOutcome.Solved, result.Outcome);
            Assert.True(Solver.Replay(room, Spawn(room), result.Moves));
        }

        [Fact]
        public void FactoryTwoBoxesTwoTargetsIsSolved()
        {
            var room = Blueprint.Parse("factory", Factory).Room;
            var result = Solver.Solve(room, Spawn(room), 500000);
            Assert.Equal(SolveOutcome.Solved, result.Outcome);
        }

        [Fact]
        public void DeadlockedBoxIsProvenUnsolvable()
        {
            var room = Blueprint.Parse("dead", new[]
            {
                "#####",
                "#.PT#",
                "#B..#",
                "#####",
            }).Room;
            var result = Solver.Solve(room, Spawn(room), 100000);
            Assert.Equal(SolveOutcome.Unsolvable, result.Outcome);
        }

        [Fact]
        public void TinyBudgetReportsBudgetReachedNotUnsolvable()
        {
            var room = Blueprint.Parse("warehouse", Warehouse).Room;
            var result = Solver.Solve(room, Spawn(room), 3);
            Assert.Equal(SolveOutcome.BudgetReached, result.Outcome);
        }

        [Fact]
        public void NoTargetIsUnsolvable()
        {
            var room = Blueprint.Parse("n", new[] { "####", "#PB#", "####" }).Room;
            Assert.Equal(SolveOutcome.Unsolvable, Solver.Solve(room, Spawn(room), 1000).Outcome);
        }

        [Fact]
        public void ReplayRejectsIllegalMove()
        {
            var room = Blueprint.Parse("warehouse", Warehouse).Room;
            Assert.False(Solver.Replay(room, Spawn(room), new[] { Cell.Up, Cell.Up, Cell.Up, Cell.Up, Cell.Up, Cell.Up }));
        }
    }
}
