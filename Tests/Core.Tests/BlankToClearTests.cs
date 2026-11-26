using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class BlankToClearTests
    {
        static RoomDraftModel BlankRoom()
        {
            var room = new Room("mine", 7, 5);
            for (int x = 0; x < 7; x++)
                for (int y = 0; y < 5; y++)
                    room.SetTerrain(new Cell(x, y), x == 0 || y == 0 || x == 6 || y == 4 ? TerrainCategories.Wall : TerrainCategories.Floor);
            room.SetTerrain(new Cell(0, 2), TerrainCategories.Door, "entry");
            room.SetTerrain(new Cell(6, 2), TerrainCategories.Door, "exit");
            var doors = new[]
            {
                new DoorInfo("entry", new Cell(0, 2), Cell.Left, "entry"),
                new DoorInfo("exit", new Cell(6, 2), Cell.Right, "exit"),
            };
            return new RoomDraftModel(room, doors, "entry");
        }

        static RoomDraftModel Build(CommandHistory history)
        {
            var draft = BlankRoom();
            history.Execute(new SetSpawnCommand(draft));
            history.Execute(new PlaceObjectCommand(draft, "box-1", ObjectCategories.Box, new Cell(3, 2)));
            history.Execute(new PlaceObjectCommand(draft, "target-1", ObjectCategories.Target, new Cell(5, 2)));
            history.Execute(new AddRecoveryItemCommand(draft, new RecoveryItem("recovery-1", "warehouse-lift", 3.7f)));
            return draft;
        }

        [Fact]
        public void BlankRoomFromCommandsPassesPublishValidation()
        {
            var draft = Build(new CommandHistory());
            var issues = RoomValidator.ValidateDraft(draft, ValidationStage.Publish);
            Assert.DoesNotContain(issues, i => i.Severity == Severity.Error);
        }

        [Fact]
        public void BlankRoomBeforeEditingOnlyHasHintsInDraft()
        {
            var issues = RoomValidator.ValidateDraft(BlankRoom(), ValidationStage.Draft);
            Assert.DoesNotContain(issues, i => i.Severity == Severity.Error);
            Assert.Contains(issues, i => i.RuleId == "spawn.missing");
            Assert.Contains(issues, i => i.RuleId == "recovery.none");
        }

        [Fact]
        public void RoomSurvivesFileRoundTripWithSameHash()
        {
            var draft = Build(new CommandHistory());
            var doc = new RoomDocument(draft.Room, draft.Doors, draft.PrimaryEntranceId, draft.RecoveryItems);
            var back = RoomCodec.Decode(RoomCodec.Encode(doc));
            Assert.Equal(ContentHash.Of(draft.Room), ContentHash.Of(back.Room));
            Assert.Single(back.RecoveryItems);
        }

        [Fact]
        public void SolverConfirmsTheBuiltRoomAndReplayClearsIt()
        {
            var draft = Build(new CommandHistory());
            var start = draft.Room.ObjectsOf(ObjectCategories.Spawn).Single().Cell;
            var verdict = Solver.Judge(draft.Room, start, 100000);
            Assert.Equal(SolveVerdict.Confirmed, verdict.Verdict);
            Assert.True(Solver.Replay(draft.Room, start, verdict.Moves));
        }

        [Fact]
        public void PlayingEntersExploresGrantsItemAndRecoveryRestoresTheBox()
        {
            var draft = Build(new CommandHistory());
            var room = draft.Room;
            var progress = new PlayProgress();
            Assert.True(progress.Enter(room.Id, draft.RecoveryItems.Single()));
            Assert.Equal("recovery-1", progress.Bag.Pending);
            progress.Bag.Accept();

            var boxes = room.ObjectsOf(ObjectCategories.Box).ToDictionary(b => b.Id, b => b.Cell);
            var session = new RecoverySession(room, boxes, new Cell(2, 2), new Cell(1, 2));
            var push = PushRules.TryPush(session, new Cell(2, 2), Cell.Right);
            Assert.True(push.Pushed);
            Assert.Equal(new Cell(4, 2), session.Boxes["box-1"]);

            session.Start(draft.RecoveryItems.Single().Duration);
            Assert.False(session.CanPush());
            session.Advance(draft.RecoveryItems.Single().Duration);
            Assert.False(session.Recovering);
            Assert.Equal(new Cell(3, 2), session.Boxes["box-1"]);

            var again = PushRules.TryPush(session, session.Player, Cell.Right);
            Assert.True(again.Moved);
            Assert.False(progress.Enter(room.Id, draft.RecoveryItems.Single()));
        }

        [Fact]
        public void SolvedStateIsReachedByPushingOntoTheTarget()
        {
            var draft = Build(new CommandHistory());
            var room = draft.Room;
            var boxes = room.ObjectsOf(ObjectCategories.Box).ToDictionary(b => b.Id, b => b.Cell);
            var session = new RecoverySession(room, boxes, new Cell(2, 2), new Cell(1, 2));
            Assert.False(PushRules.Solved(room, session.Boxes));
            var player = new Cell(2, 2);
            for (int i = 0; i < 2; i++)
                player = PushRules.TryPush(session, player, Cell.Right).Player;
            Assert.True(PushRules.Solved(room, session.Boxes));
        }

        [Fact]
        public void UndoingEveryCommandReturnsToABlankRoom()
        {
            var history = new CommandHistory();
            var draft = Build(history);
            while (history.Undo()) { }
            Assert.Empty(draft.Room.Objects);
            Assert.Empty(draft.RecoveryItems);
            Assert.Contains(RoomValidator.ValidateDraft(draft, ValidationStage.Publish), i => i.RuleId == "recovery.none");
        }
    }
}
