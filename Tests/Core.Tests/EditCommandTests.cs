using System;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class EditCommandTests
    {
        static RoomDraftModel Blank()
        {
            var room = new Room("blank", 5, 5);
            for (int x = 0; x < 5; x++)
                for (int y = 0; y < 5; y++)
                    room.SetTerrain(new Cell(x, y), x == 0 || y == 0 || x == 4 || y == 4 ? TerrainCategories.Wall : TerrainCategories.Floor);
            return new RoomDraftModel(room);
        }

        [Fact]
        public void BlankDraftHasNoRecoveryItemAndNoDevice()
        {
            var draft = Blank();
            Assert.Empty(draft.RecoveryItems);
            Assert.Empty(draft.Room.Objects);
        }

        [Fact]
        public void MissingRecoveryItemIsHintInDraftAndErrorOnPublish()
        {
            var draft = Blank();
            var d = RoomValidator.ValidateDraft(draft, ValidationStage.Draft);
            var p = RoomValidator.ValidateDraft(draft, ValidationStage.Publish);
            Assert.Equal(Severity.Hint, d.Single(i => i.RuleId == "recovery.none").Severity);
            Assert.Equal(Severity.Error, p.Single(i => i.RuleId == "recovery.none").Severity);
        }

        [Fact]
        public void AddingRecoveryItemClearsTheIssue()
        {
            var draft = Blank();
            var history = new CommandHistory();
            history.Execute(new AddRecoveryItemCommand(draft, new RecoveryItem("recovery-1", "warehouse-lift", 3.7f)));
            Assert.DoesNotContain(RoomValidator.ValidateDraft(draft, ValidationStage.Publish), i => i.RuleId == "recovery.none");
            Assert.True(draft.Room.Ids.Contains("recovery-1"));
        }

        [Fact]
        public void UndoRemovesRecoveryItemAndReleasesItsId()
        {
            var draft = Blank();
            var history = new CommandHistory();
            history.Execute(new AddRecoveryItemCommand(draft, new RecoveryItem("recovery-1", "warehouse-lift", 3.7f)));
            history.Undo();
            Assert.Empty(draft.RecoveryItems);
            Assert.False(draft.Room.Ids.Contains("recovery-1"));
        }

        [Fact]
        public void NonPositiveDurationIsRejectedWithoutHistoryEntry()
        {
            var draft = Blank();
            var history = new CommandHistory();
            Assert.Throws<InvalidOperationException>(() =>
                history.Execute(new AddRecoveryItemCommand(draft, new RecoveryItem("recovery-1", "s", 0f))));
            Assert.False(history.CanUndo);
            Assert.Equal(0, history.Revision);
        }

        [Fact]
        public void DuplicateIdIsRejected()
        {
            var draft = Blank();
            var history = new CommandHistory();
            history.Execute(new PlaceObjectCommand(draft, "box-1", ObjectCategories.Box, new Cell(2, 2)));
            Assert.Throws<InvalidOperationException>(() =>
                history.Execute(new AddRecoveryItemCommand(draft, new RecoveryItem("box-1", "s", 3f))));
        }

        [Fact]
        public void SetDurationChangesAndUndoRestores()
        {
            var draft = Blank();
            var history = new CommandHistory();
            history.Execute(new AddRecoveryItemCommand(draft, new RecoveryItem("recovery-1", "s", 3f)));
            history.Execute(new SetRecoveryDurationCommand(draft, "recovery-1", 5f));
            Assert.Equal(5f, draft.RecoveryItems.Single().Duration);
            history.Undo();
            Assert.Equal(3f, draft.RecoveryItems.Single().Duration);
        }

        [Fact]
        public void PlaceObjectNeedsFloorAndFreeCell()
        {
            var draft = Blank();
            var history = new CommandHistory();
            Assert.Throws<InvalidOperationException>(() =>
                history.Execute(new PlaceObjectCommand(draft, "box-1", ObjectCategories.Box, new Cell(0, 0))));
            history.Execute(new PlaceObjectCommand(draft, "box-1", ObjectCategories.Box, new Cell(2, 2)));
            Assert.Throws<InvalidOperationException>(() =>
                history.Execute(new PlaceObjectCommand(draft, "target-1", ObjectCategories.Target, new Cell(2, 2))));
            Assert.Single(draft.Room.Objects);
        }

        [Fact]
        public void SetSpawnMovesTheSingleSpawn()
        {
            var draft = Blank();
            var history = new CommandHistory();
            history.Execute(new SetSpawnCommand(draft, new Cell(1, 1)));
            history.Execute(new SetSpawnCommand(draft, new Cell(3, 3)));
            Assert.Equal(new Cell(3, 3), draft.Room.ObjectsOf(ObjectCategories.Spawn).Single().Cell);
            history.Undo();
            Assert.Equal(new Cell(1, 1), draft.Room.ObjectsOf(ObjectCategories.Spawn).Single().Cell);
            history.Undo();
            Assert.Empty(draft.Room.ObjectsOf(ObjectCategories.Spawn));
        }

        [Fact]
        public void UndoToSavePointClearsDirty()
        {
            var draft = Blank();
            var history = new CommandHistory();
            history.MarkSaved();
            history.Execute(new PlaceObjectCommand(draft, "box-1", ObjectCategories.Box, new Cell(2, 2)));
            Assert.True(history.Dirty);
            history.Undo();
            Assert.False(history.Dirty);
        }
    }
}
