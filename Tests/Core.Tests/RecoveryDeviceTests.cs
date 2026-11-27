/**
 * @file RecoveryDeviceTests.cs
 * @brief 覆盖恢复装置放置约束、ID 原子更新、历史和扩建。
 * EditHistoryRestoresDeviceAndId()：移动、旋转、删除可逐步撤销重做。
 * PlacementRejectsInvalidAnchor(x, y)：室内、角格和隔墙一格的落点均拒绝。
 * ReplacementIsExplicitAndIdCollisionIsAtomic()：已有装置需显式替换，ID 冲突时保留原装置。
 * MissingDeviceMatchesSpawnSeverity()：缺装置的草稿提示与发布错误同出生点一致。
 * CodecRoundTripPreservesWallAnchorAndDetails()：编解码保留房间装置全部字段。
 * DetailsAndParametersAreUndoable()：编辑只影响指定字段，并可撤销重做。
 * InvalidDetailsDoNotChangeHistory(duration)：非法时长拒绝整条命令。
 * BrushExpansionPreservesDeviceAndHistory()：扩建后锚点随边界，撤销后装置命令仍操作当前房间。
 */
using System;
using Xunit;

namespace Sokoban.Core.Tests
{
    public sealed class RecoveryDeviceTests
    {
        readonly DraftHolder draft = new DraftHolder(new RoomDraftModel(new Room("room", 8, 5)));
        readonly CommandHistory history = new CommandHistory();

        static RecoveryDevice Device(string id, Cell anchor) =>
            new RecoveryDevice(id, RecoveryDeviceKind.Forklift, anchor, 1, "warehouse-lift", 3.7f);

        [Fact]
        public void EditHistoryRestoresDeviceAndId()
        {
            var initial = Device("lift", new Cell(-1, 2));
            history.Execute(RecoveryDeviceCommands.Place(draft, initial));
            Assert.Equal(initial.Anchor, draft.Draft.Room.RecoveryDeviceFloor);
            history.Execute(RecoveryDeviceCommands.Move(draft, new Cell(8, 2)));
            Assert.False(draft.Draft.Room.IsRecoveryDeviceFloor(initial.Anchor));
            Assert.True(draft.Draft.Room.IsRecoveryDeviceFloor(new Cell(8, 2)));
            history.Execute(RecoveryDeviceCommands.Rotate(draft, -2));
            Assert.Equal(Cell.Left, draft.Draft.RecoveryDevice.EntryDirection);
            Assert.Equal(new Cell(8, 2), draft.Draft.Room.RecoveryDeviceFloor);
            history.Execute(RecoveryDeviceCommands.Delete(draft));
            Assert.Null(draft.Draft.RecoveryDevice);
            Assert.Null(draft.Draft.Room.RecoveryDeviceFloor);
            Assert.False(draft.Draft.Room.Ids.Contains(initial.Id));

            Assert.True(history.Undo());
            Assert.True(draft.Draft.Room.Ids.Contains(initial.Id));
            Assert.Equal(new Cell(8, 2), draft.Draft.RecoveryDevice.Anchor);
            Assert.Equal(new Cell(8, 2), draft.Draft.Room.RecoveryDeviceFloor);
            Assert.True(history.Undo());
            Assert.Equal(1, draft.Draft.RecoveryDevice.Rotation);
            Assert.True(history.Undo());
            Assert.Same(initial, draft.Draft.RecoveryDevice);
            Assert.Equal(initial.Anchor, draft.Draft.Room.RecoveryDeviceFloor);
            Assert.True(history.Undo());
            Assert.Null(draft.Draft.RecoveryDevice);
            Assert.Null(draft.Draft.Room.RecoveryDeviceFloor);
            for (int step = 0; step < 4; step++) Assert.True(history.Redo());
            Assert.Null(draft.Draft.RecoveryDevice);
            Assert.Null(draft.Draft.Room.RecoveryDeviceFloor);
        }

        [Theory]
        [InlineData(0, 2)]
        [InlineData(-1, -1)]
        [InlineData(8, 5)]
        [InlineData(-2, 2)]
        public void PlacementRejectsInvalidAnchor(int x, int y)
        {
            Assert.Throws<InvalidOperationException>(() => history.Execute(
                RecoveryDeviceCommands.Place(draft, Device("lift", new Cell(x, y)))));
            Assert.False(history.CanUndo);
        }

        [Fact]
        public void ReplacementIsExplicitAndIdCollisionIsAtomic()
        {
            var initial = Device("lift", new Cell(-1, 2));
            var replacement = Device("arm", new Cell(8, 1));
            draft.Draft.Room.Ids.Add("taken", "test");
            history.Execute(RecoveryDeviceCommands.Place(draft, initial));
            Assert.Throws<InvalidOperationException>(() => history.Execute(RecoveryDeviceCommands.Place(draft, replacement)));
            Assert.Throws<ArgumentException>(() => history.Execute(RecoveryDeviceCommands.Place(
                draft, Device("taken", new Cell(8, 1)), replaceExisting: true)));
            Assert.Same(initial, draft.Draft.RecoveryDevice);
            Assert.Equal(1, history.Revision);

            history.Execute(RecoveryDeviceCommands.Place(draft, replacement, replaceExisting: true));
            Assert.False(draft.Draft.Room.Ids.Contains(initial.Id));
            Assert.True(history.Undo());
            Assert.Same(initial, draft.Draft.RecoveryDevice);
            Assert.False(draft.Draft.Room.Ids.Contains(replacement.Id));
        }

        [Fact]
        public void MissingDeviceMatchesSpawnSeverity()
        {
            Assert.Equal(Severity.Hint, RecoveryDeviceRules.Validate(draft.Draft, ValidationStage.Draft).Severity);
            Assert.Equal(Severity.Error, RecoveryDeviceRules.Validate(draft.Draft, ValidationStage.Publish).Severity);
        }

        [Fact]
        public void CodecRoundTripPreservesWallAnchorAndDetails()
        {
            var initial = new RecoveryDevice("arm", RecoveryDeviceKind.Arm, new Cell(8, 2), 3, "factory-belt", 0.000125f,
                new System.Collections.Generic.Dictionary<string, float> { ["length"] = 1.625123f },
                "钥匙", "说明", "提示");
            history.Execute(RecoveryDeviceCommands.Place(draft, initial));
            string text = RoomCodec.Encode(draft.Draft);
            var model = RoomCodec.Decode(text);
            var actual = model.RecoveryDevice;
            Assert.Equal(text, RoomCodec.Encode(model));
            Assert.Equal(initial.Id, actual.Id);
            Assert.Equal(initial.Kind, actual.Kind);
            Assert.Equal(initial.Anchor, actual.Anchor);
            Assert.Equal(initial.Anchor, model.Room.RecoveryDeviceFloor);
            Assert.False(model.Room.Inside(initial.Anchor));
            Assert.Equal(TerrainCategories.Void, model.Room.TerrainAt(initial.Anchor).CategoryId);
            Assert.Equal(initial.Rotation, actual.Rotation);
            Assert.Equal(initial.ScriptRef, actual.ScriptRef);
            Assert.Equal(initial.Duration, actual.Duration);
            Assert.Equal(1.625123f, actual.Params["length"]);
            Assert.Equal(initial.ItemName, actual.ItemName);
            Assert.Equal(initial.ItemDescription, actual.ItemDescription);
            Assert.Equal(initial.ItemAcquiredHint, actual.ItemAcquiredHint);
            Assert.True(model.Room.Ids.Contains(actual.Id));
            Assert.Throws<FormatException>(() => RoomCodec.Decode(text.Replace("\"x\":8", "\"x\":7")));
        }

        [Fact]
        public void DecorationBlocksDeviceMoveAndInvalidSerializedAnchor()
        {
            var initial = Device("lift", new Cell(-1, 2));
            var blocked = new Cell(-1, 3);
            history.Execute(RecoveryDeviceCommands.Place(draft, initial));
            draft.Draft.Decorations.Add(new Decoration("decor", blocked, new AppearanceRef("TreeStump")));
            Assert.Throws<InvalidOperationException>(() => history.Execute(RecoveryDeviceCommands.Move(draft, blocked)));
            Assert.Equal(initial.Anchor, draft.Draft.Room.RecoveryDeviceFloor);
            Assert.Equal(1, history.Revision);

            string text = RoomCodec.Encode(draft.Draft);
            Assert.Throws<FormatException>(() => RoomCodec.Decode(text.Replace("\"x\":-1,\"y\":2", "\"x\":-1,\"y\":3")));
        }

        [Fact]
        public void DetailsAndParametersAreUndoable()
        {
            var initial = Device("lift", new Cell(-1, 2));
            history.Execute(RecoveryDeviceCommands.Place(draft, initial));
            history.Execute(RecoveryDeviceCommands.SetParam(draft, "travel", 2.5f));
            history.Execute(RecoveryDeviceCommands.SetDetails(draft, 2f, "钥匙", "说明", "已获得"));
            var modified = draft.Draft.RecoveryDevice;
            Assert.Equal(initial.Id, modified.Id);
            Assert.Equal(initial.Kind, modified.Kind);
            Assert.Equal(initial.Anchor, modified.Anchor);
            Assert.Equal(initial.Rotation, modified.Rotation);
            Assert.Equal(initial.ScriptRef, modified.ScriptRef);
            Assert.Equal(2.5f, modified.Params["travel"]);
            Assert.Equal(2f, modified.Duration);
            Assert.Equal("钥匙", modified.ItemName);
            Assert.Equal("说明", modified.ItemDescription);
            Assert.Equal("已获得", modified.ItemAcquiredHint);
            Assert.True(history.Undo());
            Assert.Equal(initial.Duration, draft.Draft.RecoveryDevice.Duration);
            Assert.True(history.Undo());
            Assert.Same(initial, draft.Draft.RecoveryDevice);
            Assert.True(history.Redo());
            Assert.True(history.Redo());
            Assert.Equal("钥匙", draft.Draft.RecoveryDevice.ItemName);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void InvalidDetailsDoNotChangeHistory(float duration)
        {
            var initial = Device("lift", new Cell(-1, 2));
            history.Execute(RecoveryDeviceCommands.Place(draft, initial));
            Assert.Throws<ArgumentOutOfRangeException>(() => history.Execute(
                RecoveryDeviceCommands.SetDetails(draft, duration, "name", "", "")));
            Assert.Same(initial, draft.Draft.RecoveryDevice);
            Assert.Equal(1, history.Revision);
        }

        [Fact]
        public void BrushExpansionPreservesDeviceAndHistory()
        {
            var initial = Device("lift", new Cell(8, 2));
            history.Execute(RecoveryDeviceCommands.Place(draft, initial));
            var plan = BrushPlanner.Preflight(draft.Draft, new[] { new Cell(8, 3) }, BrushTool.Wall, 16);
            history.Execute(plan.CreateCommand(draft));
            Assert.Equal(new Cell(9, 2), draft.Draft.RecoveryDevice.Anchor);
            Assert.True(history.Undo());
            Assert.Same(initial, draft.Draft.RecoveryDevice);
            Assert.True(history.Undo());
            Assert.Null(draft.Draft.RecoveryDevice);
        }
    }
}
