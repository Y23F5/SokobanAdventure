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
        public void MissingRecoveryDeviceIsHintInDraftAndErrorOnPublish()
        {
            var draft = Blank();
            var d = RoomValidator.ValidateDraft(draft, ValidationStage.Draft);
            var p = RoomValidator.ValidateDraft(draft, ValidationStage.Publish);
            Assert.Equal(Severity.Hint, d.Single(i => i.RuleId == "recoveryDevice.missing").Severity);
            Assert.Equal(Severity.Error, p.Single(i => i.RuleId == "recoveryDevice.missing").Severity);
        }

        [Fact]
        public void OnlyPlacingRecoveryDeviceClearsTheMissingDeviceIssue()
        {
            var draft = Blank();
            var history = new CommandHistory();
            history.Execute(new AddRecoveryItemCommand(draft, new RecoveryItem("recovery-1", "warehouse-lift", 3.7f)));
            var missing = Assert.Single(RoomValidator.ValidateDraft(draft, ValidationStage.Publish),
                i => i.RuleId == "recoveryDevice.missing");
            Assert.Equal("缺少恢复装置", missing.Message);
            var device = new RecoveryDevice("device", RecoveryDeviceKind.Forklift, new Cell(-1, 2), 1, "warehouse-lift", 3.7f);
            history.Execute(RecoveryDeviceCommands.Place(new DraftHolder(draft), device));
            Assert.DoesNotContain(RoomValidator.ValidateDraft(draft, ValidationStage.Publish), i => i.RuleId == "recoveryDevice.missing");
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
        public void SetSpawnIsIdempotentAndUndoable()
        {
            var draft = Blank();
            draft.Room.SetTerrain(new Cell(0, 2), TerrainCategories.Door, "entry");
            draft.Doors.ToList();
            draft = new RoomDraftModel(draft.Room, new[] { new DoorInfo("entry", new Cell(0, 2), Cell.Left, "entry") }, "entry");
            var history = new CommandHistory();
            history.Execute(new SetSpawnCommand(draft));
            history.Execute(new SetSpawnCommand(draft));
            Assert.Single(draft.Room.ObjectsOf(ObjectCategories.Spawn));
            history.Undo();
            Assert.Single(draft.Room.ObjectsOf(ObjectCategories.Spawn));
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
