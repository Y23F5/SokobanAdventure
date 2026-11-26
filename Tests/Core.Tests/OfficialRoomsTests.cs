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

        static RoomDraftModel Source(string id, string[] rows)
        {
            var r = Blueprint.Parse(id, rows);
            var items = new List<RecoveryItem> { new RecoveryItem("recovery-1", id + "-recovery", 3.7f) };
            return new RoomDraftModel(r.Room, r.Doors, r.Doors.First(d => d.Role == "entry").Id, null, items);
        }

        static RoomDraftModel RoundTrip(string id, string[] rows) => RoomCodec.Decode(RoomCodec.Encode(Source(id, rows)));

        [Theory]
        [InlineData("warehouse", "EEENEEE")]
        [InlineData("factory", "EENWNENEEEWWWSSENWNEEWSSSEEEENNE")]
        public void DocumentedRouteClearsRoundTrippedRoomAndReachesExit(string id, string route)
        {
            var draft = RoundTrip(id, id == "warehouse" ? Warehouse : Factory);
            var room = draft.Room;
            var player = room.ObjectsOf(ObjectCategories.Spawn).Single().Cell;
            var boxes = room.ObjectsOf(ObjectCategories.Box).ToDictionary(box => box.Id, box => box.Cell);
            var session = new RecoverySession(room, boxes, player, null);

            Assert.False(PushRules.Solved(room, session.Boxes));
            for (int i = 0; i < route.Length; i++)
            {
                var direction = route[i] switch
                {
                    'E' => Cell.Right,
                    'W' => Cell.Left,
                    'N' => Cell.Up,
                    'S' => Cell.Down,
                    _ => throw new System.ArgumentException("Invalid direction in documented route", nameof(route)),
                };
                var step = PushRules.TryPush(session, player, direction);
                Assert.True(step.Moved, $"{id}: step {i + 1} ({route[i]}) blocked at {player}");
                player = step.Player;
            }

            Assert.True(PushRules.Solved(room, session.Boxes));
            Assert.Equal(draft.Doors.Single(door => door.Role == "exit").Cell, player);
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
