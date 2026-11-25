using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class SolverVerdictTests
    {
        static Room Room(params string[] rows) => Blueprint.Parse("r", rows).Room;

        static Cell Spawn(Room r) => r.ObjectsOf(ObjectCategories.Spawn).Single().Cell;

        static readonly string[] Open = { "########", "#P..BT.#", "########" };

        [Fact]
        public void SolutionWithoutWindowIsConfirmed()
        {
            var room = Room(Open);
            var result = Solver.Judge(room, Spawn(room), 10000);
            Assert.Equal(SolveVerdict.Confirmed, result.Verdict);
            Assert.True(Solver.Replay(room, Spawn(room), result.Moves));
        }

        [Fact]
        public void SolutionNeedingTheWindowIsOptimistic()
        {
            var room = Room("########", "#PV.BT.#", "########");
            var result = Solver.Judge(room, Spawn(room), 10000);
            Assert.Equal(SolveVerdict.Optimistic, result.Verdict);
            Assert.NotEmpty(result.Moves);
        }

        [Fact]
        public void ExhaustedSearchWithoutOpenablesIsConfirmedUnsolvable()
        {
            var room = Room("#####", "#.PT#", "#B..#", "#####");
            Assert.Equal(SolveVerdict.Unsolvable, Solver.Judge(room, Spawn(room), 10000).Verdict);
        }

        [Fact]
        public void BudgetExhaustionIsUnverifiable()
        {
            var room = Room(Open);
            Assert.Equal(SolveVerdict.Unverifiable, Solver.Judge(room, Spawn(room), 2).Verdict);
        }

        [Fact]
        public void UnsimulatableContentIsUnverifiable()
        {
            var room = Room(Open);
            Assert.Equal(SolveVerdict.Unverifiable, Solver.Judge(room, Spawn(room), 10000, true).Verdict);
        }

        [Fact]
        public void NoSolutionEvenWithWindowIsUnverifiableNotUnsolvable()
        {
            var room = Room("########", "#PV.B#T#", "########");
            Assert.Equal(SolveVerdict.Unverifiable, Solver.Judge(room, Spawn(room), 10000).Verdict);
        }

        [Fact]
        public void WindowNeverLetsABoxEnterAnOpenableCell()
        {
            var room = Room("########", "#P.BVT.#", "########");
            Assert.NotEqual(SolveVerdict.Optimistic, Solver.Judge(room, Spawn(room), 10000).Verdict);
        }
    }
}
