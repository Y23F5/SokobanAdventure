using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class OfficialRoomsTests
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

        static IReadOnlyList<Cell> Moves(string text) => text.Select(c =>
            c == 'E' ? Cell.Right : c == 'W' ? Cell.Left : c == 'N' ? Cell.Up : Cell.Down).ToList();

        static RoomDraftModel Source(string id, string[] rows)
        {
            var r = Blueprint.Parse(id, rows);
            var items = new List<RecoveryItem> { new RecoveryItem("recovery-1", id + "-recovery", 3.7f) };
            return new RoomDraftModel(r.Room, r.Doors, r.Doors.First(d => d.Role == "entry").Id, null, items);
        }

        static RoomDraftModel RoundTrip(string id, string[] rows) => RoomCodec.Decode(RoomCodec.Encode(Source(id, rows)));

        static Cell Spawn(Room r) => r.ObjectsOf(ObjectCategories.Spawn).Single().Cell;

        [Fact]
        public void WarehouseSurvivesRoundTripAndDocumentedSolutionReplays()
        {
            var room = RoundTrip("warehouse", Warehouse).Room;
            Assert.True(Solver.Replay(room, Spawn(room), Moves("EEE")));
        }

        [Fact]
        public void FactorySurvivesRoundTripAndSolverFindsASolution()
        {
            var room = RoundTrip("factory", Factory).Room;
            var result = Solver.Solve(room, Spawn(room), 500000);
            Assert.Equal(SolveOutcome.Solved, result.Outcome);
            Assert.True(Solver.Replay(room, Spawn(room), result.Moves));
        }

        [Fact]
        public void RoundTripKeepsContentHash()
        {
            Assert.Equal(ContentHash.Of(Source("factory", Factory)), ContentHash.Of(RoundTrip("factory", Factory)));
        }

        [Fact]
        public void BothRoomsPassPublishValidationAfterRoundTrip()
        {
            foreach (var (id, rows) in new[] { ("warehouse", Warehouse), ("factory", Factory) })
            {
                var doc = RoundTrip(id, rows);
                var entry = doc.Doors.First(d => d.Role == "entry");
                var issues = RoomValidator.Validate(doc.Room, ValidationStage.Publish, entry.Cell - entry.Outward);
                Assert.DoesNotContain(issues, i => i.Severity == Severity.Error && i.RuleId != "recovery.none");
            }
        }
    }
}
