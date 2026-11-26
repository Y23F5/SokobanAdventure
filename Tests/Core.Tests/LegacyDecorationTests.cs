using System;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class LegacyDecorationTests
    {
        static LegacyRoomData Warehouse() => new LegacyRoomData
        {
            LevelId = "warehouse",
            Width = 9,
            Height = 7,
            RowsNorthToSouth = new[] { "#########", "#.......#", "#.......#", ".........", "#.......#", "#.......#", "#########" },
            Doors = new[]
            {
                new LegacyDoor("entry", new Cell(0, 3), "W", "entry"),
                new LegacyDoor("exit", new Cell(8, 3), "E", "exit"),
            },
            PrimaryEntranceId = "entry",
            Boxes = new[] { new LegacyEntity("box-1", new Cell(4, 2), new Cell(4, 2)) },
            Targets = new[] { new LegacyEntity("target-1", new Cell(6, 2)) },
            Spawn = new LegacyEntity("spawn", new Cell(2, 2)),
            Decorations = new[] { new LegacyDecoration("deco-1", new Cell(5, 4), "crate", "models/crate") },
        };

        static RoomDraftModel Draft(Action<LegacyRoomData> edit = null)
        {
            var data = Warehouse();
            edit?.Invoke(data);
            return LegacyRoomAdapter.ToDraft(data);
        }

        static string[] Rules(RoomDraftModel draft) =>
            RoomValidator.ValidateDraft(draft, ValidationStage.Publish).Select(i => i.RuleId + "@" + i.Location).ToArray();

        [Fact]
        public void DecorationBecomesCustomObjectWithKindAndAppearance()
        {
            var o = Draft().Room.ObjectAt(new Cell(5, 4));
            Assert.Equal(ObjectCategories.Custom, o.CategoryId);
            Assert.Equal("deco-1", o.Id);
            Assert.Equal("crate", o.DecorationKind);
            Assert.Equal("models/crate", o.AppearanceRef);
        }

        [Fact]
        public void PlainObjectsHaveNullDecorationFields()
        {
            var o = Draft().Room.ObjectAt(new Cell(4, 2));
            Assert.Null(o.DecorationKind);
            Assert.Null(o.AppearanceRef);
        }

        [Fact]
        public void DecorationIdSharesTheRoomIdSpace()
        {
            Assert.True(Draft().Room.Ids.Contains("deco-1"));
        }

        [Fact]
        public void DecorationWithBoxIdIsFormatException()
        {
            Assert.Throws<FormatException>(() => Draft(d => d.Decorations = new[] { new LegacyDecoration("box-1", new Cell(5, 4)) }));
        }

        [Fact]
        public void DecorationWithDoorIdIsFormatException()
        {
            Assert.Throws<FormatException>(() => Draft(d => d.Decorations = new[] { new LegacyDecoration("exit", new Cell(5, 4)) }));
        }

        [Fact]
        public void TwoDecorationsWithSameIdAreFormatException()
        {
            Assert.Throws<FormatException>(() => Draft(d => d.Decorations = new[]
            {
                new LegacyDecoration("x", new Cell(5, 4)), new LegacyDecoration("x", new Cell(5, 5)),
            }));
        }

        [Fact]
        public void WhitespaceDecorationIdIsFormatException()
        {
            Assert.Throws<FormatException>(() => Draft(d => d.Decorations = new[] { new LegacyDecoration("  ", new Cell(5, 4)) }));
        }

        [Fact]
        public void WhitespaceBoxIdIsFormatException()
        {
            Assert.Throws<FormatException>(() => Draft(d => d.Boxes = new[] { new LegacyEntity(" ", new Cell(4, 2)) }));
        }

        [Fact]
        public void WhitespaceDoorIdIsFormatException()
        {
            Assert.Throws<FormatException>(() => Draft(d => d.Doors = new[] { new LegacyDoor(" ", new Cell(0, 3), "W", "entry") }));
        }

        [Fact]
        public void CleanDecorationHasNoObjectIssues()
        {
            Assert.DoesNotContain(Rules(Draft()), r => r.StartsWith("object."));
        }

        [Fact]
        public void DecorationOnWallIsNotOnFloor()
        {
            var rules = Rules(Draft(d => d.Decorations = new[] { new LegacyDecoration("deco-1", new Cell(0, 0)) }));
            Assert.Contains("object.notOnFloor@deco-1", rules);
        }

        [Fact]
        public void DecorationOutsideRoomIsNotOnFloor()
        {
            var rules = Rules(Draft(d => d.Decorations = new[] { new LegacyDecoration("deco-1", new Cell(20, 20)) }));
            Assert.Contains("object.notOnFloor@deco-1", rules);
        }

        [Fact]
        public void DecorationOnDoorCellIsNotOnFloor()
        {
            var rules = Rules(Draft(d => d.Decorations = new[] { new LegacyDecoration("deco-1", new Cell(0, 3)) }));
            Assert.Contains("object.notOnFloor@deco-1", rules);
        }

        [Fact]
        public void DecorationSharingBoxCellReportsSharedCell()
        {
            var rules = Rules(Draft(d => d.Decorations = new[] { new LegacyDecoration("deco-1", new Cell(4, 2)) }));
            Assert.Contains("object.sharedCell@deco-1", rules);
        }

        [Fact]
        public void TwoDecorationsOnOneCellReportSharedCell()
        {
            var rules = Rules(Draft(d => d.Decorations = new[]
            {
                new LegacyDecoration("a", new Cell(5, 4)), new LegacyDecoration("b", new Cell(5, 4)),
            }));
            Assert.Contains("object.sharedCell@b", rules);
        }

        [Fact]
        public void DecorationCellIsOccupiedAndRejectsPlacement()
        {
            var draft = Draft();
            Assert.True(PlacementRules.Occupied(draft, new Cell(5, 4), null));
            Assert.Equal("格子已占用", PlacementRules.Error(draft, ObjectCategories.Box, new Cell(5, 4), null));
        }

        [Fact]
        public void DecorationDoesNotBlockItselfWhenMoving()
        {
            var draft = Draft();
            Assert.False(PlacementRules.Occupied(draft, new Cell(5, 4), "deco-1"));
            Assert.Null(PlacementRules.Error(draft, ObjectCategories.Custom, new Cell(5, 4), "deco-1"));
        }

        [Fact]
        public void PlaceCommandRejectsDecorationCell()
        {
            var draft = Draft();
            Assert.Throws<InvalidOperationException>(() => new PlaceObjectCommand(draft, "box-2", ObjectCategories.Box, new Cell(5, 4)).Apply());
        }

        [Fact]
        public void MovingBoxOntoDecorationIsRejected()
        {
            var draft = Draft();
            Assert.Throws<InvalidOperationException>(() => new MoveObjectCommand(draft, "box-1", new Cell(5, 4), true).Apply());
        }

        [Fact]
        public void MovingDecorationKeepsFieldsAndRevertRestores()
        {
            var draft = Draft();
            var cmd = new MoveObjectCommand(draft, "deco-1", new Cell(3, 4), false);
            cmd.Apply();
            var moved = draft.Room.ObjectAt(new Cell(3, 4));
            Assert.Equal("crate", moved.DecorationKind);
            Assert.Equal("models/crate", moved.AppearanceRef);
            cmd.Revert();
            var back = draft.Room.ObjectAt(new Cell(5, 4));
            Assert.Equal("deco-1", back.Id);
            Assert.Equal("crate", back.DecorationKind);
            Assert.Equal("models/crate", back.AppearanceRef);
        }

        [Fact]
        public void ResizedRoomKeepsDecorationFields()
        {
            var room = Draft().Room;
            var bigger = RoomResize.Resized(room, 1, 1, 11, 9);
            var o = bigger.ObjectAt(new Cell(6, 5));
            Assert.Equal("deco-1", o.Id);
            Assert.Equal("crate", o.DecorationKind);
            Assert.Equal("models/crate", o.AppearanceRef);
        }
    }
}
