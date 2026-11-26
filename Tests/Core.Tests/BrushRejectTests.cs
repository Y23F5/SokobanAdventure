/**
 * @file BrushRejectTests.cs
 * @brief 验证整笔预检的拒绝路径：任一格非法则整笔拒绝、草稿不变、不产生撤销记录；扩建后的坐标上同样保护对象与门。
 * 覆盖对象格与门内侧格不能改墙或擦除、门格对三种工具都受保护、擦除不能越界扩建、尺寸上限、空笔划与无改动命令被拒绝，以及与旧逐点部分提交不同的整笔拒绝行为。
 */
using System;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class BrushRejectTests
    {
        static BrushPlan Plan(RoomDraftModel draft, BrushTool tool, params Cell[] cells) => BrushPlanner.Preflight(draft, cells, tool);

        [Fact]
        public void OneIllegalCellRejectsTheWholeStroke()
        {
            var holder = new DraftHolder(BrushFixture.Draft());
            var before = holder.Draft;
            var history = new CommandHistory();
            var cells = new[] { new Cell(2, 2), new Cell(3, 3) };
            Assert.NotNull(Plan(before, BrushTool.Wall, cells).Error);
            Assert.Throws<InvalidOperationException>(() => history.Execute(new BrushCommand(holder, cells, BrushTool.Wall)));
            Assert.False(history.CanUndo);
            Assert.False(history.Dirty);
            Assert.Same(before, holder.Draft);
            Assert.Equal(TerrainCategories.Floor, holder.Draft.Room.TerrainAt(new Cell(2, 2)).CategoryId);
        }

        [Fact]
        public void CreateCommandThrowsForARejectedPlan()
        {
            var holder = new DraftHolder(BrushFixture.Draft());
            var plan = Plan(holder.Draft, BrushTool.Wall, new Cell(3, 3));
            Assert.Throws<InvalidOperationException>(() => plan.CreateCommand(holder));
        }

        [Fact]
        public void RejectedStrokeDoesNotExpandTheRoom()
        {
            var holder = new DraftHolder(BrushFixture.Draft());
            var before = holder.Draft;
            var history = new CommandHistory();
            var cells = new[] { new Cell(-1, 3), new Cell(3, 3) };
            Assert.Throws<InvalidOperationException>(() => history.Execute(new BrushCommand(holder, cells, BrushTool.Wall)));
            Assert.Same(before, holder.Draft);
            Assert.Equal(7, holder.Draft.Room.Width);
            Assert.False(history.CanUndo);
        }

        [Theory]
        [InlineData(BrushTool.Wall)]
        [InlineData(BrushTool.Erase)]
        public void DoorInnerCellKeepsItsFloor(BrushTool tool)
        {
            Assert.NotNull(Plan(BrushFixture.Draft(), tool, new Cell(1, 3)).Error);
            Assert.NotNull(Plan(BrushFixture.Draft(), tool, new Cell(5, 3)).Error);
        }

        [Theory]
        [InlineData(BrushTool.Floor)]
        [InlineData(BrushTool.Wall)]
        [InlineData(BrushTool.Erase)]
        public void DoorCellIsProtectedForEveryTool(BrushTool tool)
        {
            Assert.NotNull(Plan(BrushFixture.Draft(), tool, new Cell(0, 3)).Error);
            Assert.NotNull(Plan(BrushFixture.Draft(), tool, new Cell(6, 3)).Error);
        }

        [Theory]
        [InlineData(BrushTool.Wall)]
        [InlineData(BrushTool.Erase)]
        public void ObjectCellCannotBecomeWallOrVoid(BrushTool tool)
        {
            Assert.NotNull(Plan(BrushFixture.Draft(), tool, new Cell(3, 3)).Error);
            Assert.NotNull(Plan(BrushFixture.Draft(), tool, new Cell(4, 4)).Error);
        }

        [Fact]
        public void EraseDoesNotRemoveObjects()
        {
            var draft = BrushFixture.Draft();
            Assert.NotNull(Plan(draft, BrushTool.Erase, new Cell(3, 3)).Error);
            Assert.NotNull(draft.Room.ObjectAt(new Cell(3, 3)));
        }

        [Fact]
        public void FloorOverAnObjectCellIsNoChange()
        {
            var holder = new DraftHolder(BrushFixture.Draft());
            var plan = Plan(holder.Draft, BrushTool.Floor, new Cell(3, 3));
            Assert.Null(plan.Error);
            Assert.True(plan.NoChange);
            Assert.Null(plan.CreateCommand(holder));
        }

        [Fact]
        public void EraseOutsideTheRoomIsRejectedInsteadOfExpanding()
        {
            var plan = Plan(BrushFixture.Draft(), BrushTool.Erase, new Cell(-1, 0));
            Assert.NotNull(plan.Error);
            Assert.False(plan.Expands);
        }

        [Fact]
        public void ExpandedStrokeChecksTheShiftedDoorInnerCell()
        {
            Assert.NotNull(Plan(BrushFixture.Draft(), BrushTool.Wall, new Cell(-1, 0), new Cell(1, 3)).Error);
        }

        [Fact]
        public void ExpandedStrokeChecksTheShiftedObjects()
        {
            Assert.NotNull(Plan(BrushFixture.Draft(), BrushTool.Wall, new Cell(-1, 0), new Cell(3, 3)).Error);
        }

        [Fact]
        public void SizeLimitRejectsAnOversizedRoom()
        {
            var draft = BrushFixture.Draft();
            var cells = new[] { new Cell(9, 3) };
            Assert.NotNull(BrushPlanner.Preflight(draft, cells, BrushTool.Floor, 9).Error);
            Assert.Null(BrushPlanner.Preflight(draft, cells, BrushTool.Floor, 10).Error);
        }

        [Fact]
        public void EmptyStrokeIsNoChange()
        {
            var plan = BrushPlanner.Preflight(BrushFixture.Draft(), new Cell[0], BrushTool.Wall);
            Assert.Null(plan.Error);
            Assert.True(plan.NoChange);
        }

        [Fact]
        public void NoChangeCommandIsRejectedWithoutHistory()
        {
            var holder = new DraftHolder(BrushFixture.Draft());
            var history = new CommandHistory();
            Assert.Throws<InvalidOperationException>(() => history.Execute(new BrushCommand(holder, new[] { new Cell(1, 0) }, BrushTool.Wall)));
            Assert.False(history.CanUndo);
            Assert.False(history.Dirty);
        }
    }
}
