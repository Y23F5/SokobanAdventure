/**
 * @file Stage1TransformTests.cs
 * @brief 验证放置物旋转、门方向与房间内素材偏移的 Core 数据、文件与历史。
 * Sample()：构造一扇西向入口、带状态地形、出生点、墙外装饰和共享素材的草稿。
 * 用例覆盖 v1 缺省字段、非法输入、门的内外格规则，以及笔划替换草稿后的撤销重做。
 */
using System;
using System.Linq;
using Xunit;

namespace Sokoban.Core.Tests
{
    public sealed class Stage1TransformTests
    {
        static readonly Cell DoorCell = new Cell(0, 1);
        static readonly Cell SpawnCell = new Cell(1, 1);
        static readonly Cell OpenableCell = new Cell(2, 1);

        static RoomDraftModel Sample()
        {
            var room = new Room("room", 4, 4);
            var look = new AppearanceRef("model-shared");
            room.SetTerrain(DoorCell, TerrainCategories.Door, "entry");
            room.SetAppearance(DoorCell, look);
            room.SetTerrain(SpawnCell, TerrainCategories.Floor);
            room.SetTerrain(OpenableCell, TerrainCategories.Openable, "gate", TerrainCategories.Closed);
            room.SetAppearance(OpenableCell, look);
            room.SetTerrain(new Cell(1, 2), TerrainCategories.Floor);
            room.AddObject("spawn", ObjectCategories.Spawn, SpawnCell);
            room.AddObject("box", ObjectCategories.Box, new Cell(1, 2), look);
            var draft = new RoomDraftModel(room, new[] { new DoorInfo("entry", DoorCell, Cell.Left, "entry") }, "entry");
            draft.Decorations.Add(new Decoration("decor", new Cell(-1, 3), look));
            draft.Assets.Add(new AssetDescriptor(look.Id, AssetKinds.Model, "Shared", "abc", ".glb"));
            return draft;
        }

        [Fact]
        public void V1DefaultsAreAbsentAndDecodeAsZero()
        {
            var text = RoomCodec.Encode(Sample());
            Assert.Contains("\"formatVersion\":1", text);
            Assert.DoesNotContain("\"rotation\":", text);
            Assert.DoesNotContain("\"heightOffset\":", text);
            var loaded = RoomCodec.Decode(text);
            Assert.All(loaded.Room.Objects, item => Assert.Equal(0, item.Rotation));
            Assert.Equal(0, loaded.Room.TerrainAt(OpenableCell).Rotation);
            Assert.Equal(0, loaded.Decorations.Single().Rotation);
            Assert.Equal(0f, loaded.Assets.Single().HeightOffset);
            Assert.Equal(text, RoomCodec.Encode(loaded));
        }

        [Fact]
        public void RotationsAndHeightRoundTripWithOneDoorDirection()
        {
            var holder = new DraftHolder(Sample());
            new RotateCommand(holder, "spawn", 1).Apply();
            new RotateCommand(holder, OpenableCell, -1).Apply();
            new RotateCommand(holder, "decor", 2).Apply();
            new RotateCommand(holder, DoorCell, 1).Apply();
            new SetAssetHeightCommand(holder, "model-shared", -0.1234f).Apply();

            string encoded = RoomCodec.Encode(holder.Draft);
            Assert.Contains("\"heightOffset\":-123", encoded);
            var loaded = RoomCodec.Decode(encoded);
            Assert.Equal(1, loaded.Room.ObjectAt(SpawnCell).Rotation);
            Assert.Equal(3, loaded.Room.TerrainAt(OpenableCell).Rotation);
            Assert.Equal(2, loaded.Decorations.Single().Rotation);
            Assert.Equal(Cell.Up, loaded.Doors.Single().Outward);
            Assert.Equal(2, loaded.Doors.Single().Rotation);
            Assert.Equal(0, loaded.Room.TerrainAt(DoorCell).Rotation);
            Assert.Equal(-0.123f, loaded.Assets.Single().HeightOffset);
            Assert.Equal(encoded, RoomCodec.Encode(loaded));
        }

        [Theory]
        [InlineData("-1")]
        [InlineData("4")]
        [InlineData("null")]
        [InlineData("\"1\"")]
        public void CodecRejectsMalformedRotation(string value)
        {
            string text = RoomCodec.Encode(Sample()).Replace("\"id\":\"spawn\"", "\"rotation\":" + value + ",\"id\":\"spawn\"");
            Assert.Throws<FormatException>(() => RoomCodec.Decode(text));
        }

        [Theory]
        [InlineData("2147483648")]
        [InlineData("0.5")]
        [InlineData("null")]
        public void CodecRejectsMalformedHeight(string value)
        {
            string text = RoomCodec.Encode(Sample()).Replace("\"extension\":\".glb\"", "\"heightOffset\":" + value + ",\"extension\":\".glb\"");
            Assert.Throws<FormatException>(() => RoomCodec.Decode(text));
        }

        [Fact]
        public void ResizeMoveAppearanceAndSpawnCommandsKeepRotation()
        {
            var holder = new DraftHolder(Sample());
            new RotateCommand(holder, "box", -1).Apply();
            new RotateCommand(holder, "spawn", 2).Apply();
            new RotateCommand(holder, OpenableCell, 1).Apply();
            new RotateCommand(holder, "decor", 1).Apply();
            var draft = RoomResize.Resized(holder.Draft, 1, 0, 5, 4);
            var room = draft.Room;
            Assert.Equal(1, room.TerrainAt(OpenableCell + Cell.Right).Rotation);
            Assert.Equal(1, draft.Decorations.Single().Rotation);
            Assert.Equal(Cell.Left, draft.Doors.Single().Outward);

            room.SetObjectAppearance("box", new AppearanceRef("another-look"));
            var move = new MoveObjectCommand(draft, "box", new Cell(3, 2));
            room.SetTerrain(new Cell(3, 2), TerrainCategories.Floor);
            move.Apply();
            Assert.Equal(3, room.ObjectAt(new Cell(3, 2)).Rotation);
            move.Revert();
            Assert.Equal(3, room.ObjectAt(new Cell(2, 2)).Rotation);
            var spawn = new SetSpawnCommand(draft);
            spawn.Apply();
            Assert.Equal(2, room.ObjectAt(SpawnCell + Cell.Right).Rotation);
            spawn.Revert();
            Assert.Equal(2, room.ObjectAt(SpawnCell + Cell.Right).Rotation);
        }

        [Fact]
        public void CommandsResolveCurrentDraftAcrossBrushUndoAndRedo()
        {
            var holder = new DraftHolder(Sample());
            var otherRoom = Sample();
            var history = new CommandHistory();
            var painted = new Cell(3, 3);
            history.Execute(new RotateCommand(holder, "spawn", -1));
            history.Execute(new SetAssetHeightCommand(holder, "model-shared", 0.125f));
            history.Execute(new BrushCommand(holder, new[] { painted }, BrushTool.Floor));
            history.Execute(new RotateCommand(holder, painted, 1));
            history.Execute(new SetAssetHeightCommand(holder, "model-shared", -0.25f));
            Assert.Equal(1, holder.Draft.Room.TerrainAt(painted).Rotation);
            Assert.Equal(-0.25f, holder.Draft.Assets.Single().HeightOffset);
            Assert.Equal(0f, otherRoom.Assets.Single().HeightOffset);

            Assert.True(history.Undo());
            Assert.Equal(0.125f, holder.Draft.Assets.Single().HeightOffset);
            Assert.True(history.Undo());
            Assert.True(history.Undo());
            Assert.True(history.Undo());
            Assert.True(history.Undo());
            Assert.Equal(0, holder.Draft.Room.ObjectAt(SpawnCell).Rotation);
            Assert.Equal(0f, holder.Draft.Assets.Single().HeightOffset);
            Assert.Equal(TerrainCategories.Void, holder.Draft.Room.TerrainAt(painted).CategoryId);

            for (int i = 0; i < 5; i++) Assert.True(history.Redo());
            Assert.Equal(3, holder.Draft.Room.ObjectAt(SpawnCell).Rotation);
            Assert.Equal(1, holder.Draft.Room.TerrainAt(painted).Rotation);
            Assert.Equal(-0.25f, holder.Draft.Assets.Single().HeightOffset);
        }

        [Fact]
        public void BrushRedoRestoresTheModelUsedByLaterCommands()
        {
            var holder = new DraftHolder(Sample());
            var history = new CommandHistory();
            var cell = new Cell(3, 3);
            history.Execute(new BrushCommand(holder, new[] { cell }, BrushTool.Floor));
            var modelAfterBrush = holder.Draft;
            var box = holder.Draft.Room.ObjectsOf(ObjectCategories.Box).Single();
            history.Execute(new MoveObjectCommand(modelAfterBrush, box.Id, cell));
            var expected = RoomCodec.Encode(holder.Draft);
            Assert.True(history.Undo());
            Assert.True(history.Undo());
            Assert.True(history.Redo());
            Assert.Same(modelAfterBrush, holder.Draft);
            Assert.True(history.Redo());
            Assert.Equal(expected, RoomCodec.Encode(holder.Draft));
        }

        [Theory]
        [InlineData(BrushTool.Floor, TerrainCategories.Floor)]
        [InlineData(BrushTool.Wall, TerrainCategories.Wall)]
        public void PreviewRotationAppliesToWholeBrushStrokeAndUndoesTogether(BrushTool tool, string category)
        {
            var holder = new DraftHolder(Sample());
            var existing = new Cell(3, 2);
            var added = new Cell(3, 3);
            holder.Draft.Room.SetTerrain(existing, category, rotation: 1);
            var cells = new[] { existing, added };
            var plan = BrushPlanner.Preflight(holder.Draft, cells, tool, rotation: 3);
            Assert.Null(plan.Error);
            Assert.False(plan.NoChange);
            var history = new CommandHistory();
            history.Execute(plan.CreateCommand(holder));
            foreach (var cell in cells) Assert.Equal(3, holder.Draft.Room.TerrainAt(cell).Rotation);
            Assert.True(BrushPlanner.Preflight(holder.Draft, cells, tool, rotation: 3).NoChange);
            Assert.True(history.Undo());
            Assert.False(history.CanUndo);
            Assert.Equal(1, holder.Draft.Room.TerrainAt(existing).Rotation);
            Assert.Equal(TerrainCategories.Void, holder.Draft.Room.TerrainAt(added).CategoryId);
            Assert.True(history.Redo());
            foreach (var cell in cells) Assert.Equal(3, holder.Draft.Room.TerrainAt(cell).Rotation);
        }

        [Fact]
        public void BrushRejectsInvalidPreviewWithoutMutationAndErasesRotation()
        {
            var holder = new DraftHolder(Sample());
            var point = new Cell(3, 3);
            holder.Draft.Room.SetTerrain(point, TerrainCategories.Floor, rotation: 3);
            var history = new CommandHistory();
            Assert.NotNull(BrushPlanner.Preflight(holder.Draft, new[] { point }, BrushTool.Wall, rotation: 4).Error);
            Assert.Throws<InvalidOperationException>(() =>
                history.Execute(new BrushCommand(holder, new[] { point, DoorCell }, BrushTool.Wall, rotation: 2)));
            Assert.False(history.CanUndo);
            Assert.Equal(3, holder.Draft.Room.TerrainAt(point).Rotation);
            history.Execute(new BrushCommand(holder, new[] { point }, BrushTool.Erase, rotation: 2));
            Assert.Equal(TerrainCategories.Void, holder.Draft.Room.TerrainAt(point).CategoryId);
            Assert.Equal(0, holder.Draft.Room.TerrainAt(point).Rotation);
            history.Undo();
            Assert.Equal(3, holder.Draft.Room.TerrainAt(point).Rotation);
        }

        [Fact]
        public void ObjectAndSpawnPlacementReplayPreviewRotationAfterBrushReplacesDraft()
        {
            var holder = new DraftHolder(Sample());
            holder.Draft.Room.RemoveObject("spawn");
            var point = new Cell(3, 3);
            var history = new CommandHistory();
            history.Execute(new BrushCommand(holder, new[] { point }, BrushTool.Floor, rotation: 3));
            history.Execute(new PlaceObjectCommand(holder, "new-box", ObjectCategories.Box, point, rotation: 2));
            history.Execute(new SetSpawnCommand(holder, rotation: 1));
            Assert.Equal(1, holder.Draft.Room.ObjectAt(SpawnCell).Rotation);
            Assert.Equal(2, holder.Draft.Room.ObjectAt(point).Rotation);
            for (int i = 0; i < 3; i++) Assert.True(history.Undo());
            Assert.Null(holder.Draft.Room.ObjectAt(SpawnCell));
            Assert.Null(holder.Draft.Room.ObjectAt(point));
            for (int i = 0; i < 3; i++) Assert.True(history.Redo());
            Assert.Equal(1, holder.Draft.Room.ObjectAt(SpawnCell).Rotation);
            Assert.Equal(2, holder.Draft.Room.ObjectAt(point).Rotation);
            Assert.Equal(3, holder.Draft.Room.TerrainAt(point).Rotation);
        }

        [Fact]
        public void InvalidCommandsLeaveDataAndHistoryUnchanged()
        {
            var holder = new DraftHolder(Sample());
            var history = new CommandHistory();
            Assert.Throws<InvalidOperationException>(() => history.Execute(new RotateCommand(holder, "missing", 1)));
            Assert.Throws<InvalidOperationException>(() => history.Execute(new RotateCommand(holder, DoorCell, 4)));
            Assert.Throws<InvalidOperationException>(() => history.Execute(new SetAssetHeightCommand(holder, "missing", 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SetAssetHeightCommand(holder, "model-shared", float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SetAssetHeightCommand(holder, "model-shared", float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SetAssetHeightCommand(holder, "model-shared", float.MaxValue));
            Assert.False(history.CanUndo);
            Assert.Equal(Cell.Left, holder.Draft.Doors.Single().Outward);
            Assert.Equal(0f, holder.Draft.Assets.Single().HeightOffset);
        }

        [Fact]
        public void DoorRotationIsReportedAsStructuralErrorAndUndoRepairsIt()
        {
            var holder = new DraftHolder(Sample());
            var history = new CommandHistory();
            Assert.DoesNotContain(RoomValidator.ValidateDraft(holder.Draft, ValidationStage.Publish), issue => issue.RuleId == "door.geometry");
            history.Execute(new RotateCommand(holder, "entry", 2));
            foreach (var stage in new[] { ValidationStage.Draft, ValidationStage.Publish })
            {
                var issue = RoomValidator.ValidateDraft(holder.Draft, stage).Single(i => i.RuleId == "door.geometry");
                Assert.Equal(Severity.Error, issue.Severity);
                Assert.Equal("entry", issue.Location);
                Assert.Equal("门朝向不可通行", issue.Message);
            }
            Assert.Equal(Cell.Right, RoomCodec.Decode(RoomCodec.Encode(holder.Draft)).Doors.Single().Outward);
            history.Undo();
            Assert.Equal("entry", holder.Draft.PrimaryEntranceId);
            Assert.Equal("entry", holder.Draft.Room.TerrainAt(DoorCell).EntityId);
            Assert.Equal(1, holder.Draft.Doors.Single().Rotation);
            Assert.DoesNotContain(RoomValidator.ValidateDraft(holder.Draft, ValidationStage.Publish), issue => issue.RuleId == "door.geometry");
            history.Redo();
            Assert.Equal(Cell.Right, holder.Draft.Doors.Single().Outward);
        }

        [Theory]
        [InlineData(-1, 0, true)]
        [InlineData(-2, 0, false)]
        [InlineData(-1, 1, false)]
        [InlineData(0, 0, false)]
        public void DoorAllowsInteriorExteriorVoidOnlyWithUnitCardinalDirection(int x, int y, bool valid)
        {
            var room = new Room("room", 7, 7);
            var cell = new Cell(2, 3);
            var outward = new Cell(x, y);
            room.SetTerrain(cell, TerrainCategories.Door, "entry");
            if (outward != new Cell(0, 0)) room.SetTerrain(cell - outward, TerrainCategories.Floor);
            var door = new DoorInfo("entry", cell, outward, "entry");
            var draft = new RoomDraftModel(room, new[] { door }, "entry");
            Assert.Equal(valid, !RoomValidator.ValidateDraft(draft, ValidationStage.Publish).Any(i => i.RuleId == "door.geometry"));
            Assert.Equal(valid ? 1 : -1, door.Rotation);
        }
    }
}
