/**
 * @file BrushStrokeTests.cs
 * @brief 验证整笔预检通过后的提交：墙、地板、擦除的写入，越界扩建后的平移，撤销与重做，地形实体 ID 的释放与恢复。
 * 每个用例以 BrushFixture 的草稿放进 DraftHolder，经 BrushPlanner.Preflight 预检后由 CommandHistory 执行，再断言草稿内容；撤销后持有者必须回到原草稿对象。
 */
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class BrushStrokeTests
    {
        static (DraftHolder holder, CommandHistory history) Run(BrushTool tool, params Cell[] cells)
        {
            var holder = new DraftHolder(BrushFixture.Draft());
            var history = new CommandHistory();
            var plan = BrushPlanner.Preflight(holder.Draft, cells, tool);
            Assert.Null(plan.Error);
            history.Execute(plan.CreateCommand(holder));
            return (holder, history);
        }

        static string Category(DraftHolder holder, int x, int y) => holder.Draft.Room.TerrainAt(new Cell(x, y)).CategoryId;

        [Fact]
        public void WallOnFreeFloorWritesWall()
        {
            var (holder, history) = Run(BrushTool.Wall, new Cell(2, 2));
            Assert.Equal(TerrainCategories.Wall, Category(holder, 2, 2));
            Assert.True(history.Dirty);
        }

        [Fact]
        public void FloorOnVoidInsideTheRoomWritesFloor()
        {
            var (holder, _) = Run(BrushTool.Floor, new Cell(5, 5));
            Assert.Equal(TerrainCategories.Floor, Category(holder, 5, 5));
        }

        [Fact]
        public void EraseWritesVoid()
        {
            var (holder, _) = Run(BrushTool.Erase, new Cell(2, 2));
            Assert.Equal(TerrainCategories.Void, Category(holder, 2, 2));
        }

        [Fact]
        public void StrokeWritesEveryCellAndIgnoresDuplicates()
        {
            var (holder, _) = Run(BrushTool.Wall, new Cell(1, 1), new Cell(2, 1), new Cell(2, 1), new Cell(2, 2));
            Assert.Equal(TerrainCategories.Wall, Category(holder, 1, 1));
            Assert.Equal(TerrainCategories.Wall, Category(holder, 2, 1));
            Assert.Equal(TerrainCategories.Wall, Category(holder, 2, 2));
        }

        [Fact]
        public void ExpandWestAndSouthShiftsEverything()
        {
            var holder = new DraftHolder(BrushFixture.Draft());
            var plan = BrushPlanner.Preflight(holder.Draft, new[] { new Cell(-1, -2) }, BrushTool.Floor);
            Assert.Null(plan.Error);
            Assert.Equal(1, plan.Left);
            Assert.Equal(2, plan.Bottom);
            Assert.Equal(0, plan.Right);
            Assert.Equal(0, plan.Top);
            new CommandHistory().Execute(plan.CreateCommand(holder));
            var room = holder.Draft.Room;
            Assert.Equal(8, room.Width);
            Assert.Equal(9, room.Height);
            Assert.Equal(TerrainCategories.Floor, room.TerrainAt(new Cell(0, 0)).CategoryId);
            Assert.Equal(TerrainCategories.Void, room.TerrainAt(new Cell(0, 1)).CategoryId);
            Assert.Equal("box-1", room.ObjectAt(new Cell(4, 5)).Id);
            Assert.Equal("target-1", room.ObjectAt(new Cell(5, 6)).Id);
            Assert.Equal(new Cell(1, 5), holder.Draft.Doors[0].Cell);
            Assert.Equal(new Cell(7, 5), holder.Draft.Doors[1].Cell);
            Assert.Equal("entry", room.TerrainAt(new Cell(1, 5)).EntityId);
        }

        [Fact]
        public void ExpandEastAndNorthKeepsOriginalCoordinates()
        {
            var holder = new DraftHolder(BrushFixture.Draft());
            var plan = BrushPlanner.Preflight(holder.Draft, new[] { new Cell(8, 9) }, BrushTool.Wall);
            Assert.Null(plan.Error);
            Assert.Equal(2, plan.Right);
            Assert.Equal(3, plan.Top);
            new CommandHistory().Execute(plan.CreateCommand(holder));
            var room = holder.Draft.Room;
            Assert.Equal(9, room.Width);
            Assert.Equal(10, room.Height);
            Assert.Equal(TerrainCategories.Wall, room.TerrainAt(new Cell(8, 9)).CategoryId);
            Assert.Equal("box-1", room.ObjectAt(new Cell(3, 3)).Id);
            Assert.Equal(new Cell(6, 3), holder.Draft.Doors[1].Cell);
        }

        [Fact]
        public void ExpandKeepsIdsRecoveryItemsAndExtras()
        {
            var holder = new DraftHolder(BrushFixture.Draft());
            new CommandHistory().Execute(BrushPlanner.Preflight(holder.Draft, new[] { new Cell(-1, 0) }, BrushTool.Floor).CreateCommand(holder));
            var draft = holder.Draft;
            Assert.True(draft.Room.Ids.Contains("box-1"));
            Assert.True(draft.Room.Ids.Contains("entry"));
            Assert.True(draft.Room.Ids.Contains("recovery-1"));
            Assert.Single(draft.RecoveryItems);
            Assert.Equal("x", draft.Extras["note"]);
            Assert.Equal("entry", draft.PrimaryEntranceId);
        }

        [Fact]
        public void ExpandAndWritesAreOneUndoStep()
        {
            var holder = new DraftHolder(BrushFixture.Draft());
            var before = holder.Draft;
            var history = new CommandHistory();
            var plan = BrushPlanner.Preflight(holder.Draft, new[] { new Cell(-1, -1), new Cell(0, -1), new Cell(8, 3) }, BrushTool.Floor);
            Assert.Null(plan.Error);
            history.Execute(plan.CreateCommand(holder));
            Assert.NotSame(before, holder.Draft);
            Assert.True(history.Undo());
            Assert.Same(before, holder.Draft);
            Assert.Equal(7, holder.Draft.Room.Width);
            Assert.Equal(new Cell(0, 3), holder.Draft.Doors[0].Cell);
            Assert.False(history.Dirty);
            Assert.False(history.CanUndo);
            Assert.True(history.Redo());
            Assert.Equal(10, holder.Draft.Room.Width);
            Assert.Equal(8, holder.Draft.Room.Height);
        }

        [Fact]
        public void FloorOverWallEntityReleasesTheIdAndUndoRestoresIt()
        {
            var draft = BrushFixture.Draft();
            draft.Room.SetTerrain(new Cell(2, 2), TerrainCategories.Wall, "wall-a");
            var holder = new DraftHolder(draft);
            var history = new CommandHistory();
            history.Execute(BrushPlanner.Preflight(holder.Draft, new[] { new Cell(2, 2) }, BrushTool.Floor).CreateCommand(holder));
            Assert.False(holder.Draft.Room.Ids.Contains("wall-a"));
            history.Undo();
            Assert.True(holder.Draft.Room.Ids.Contains("wall-a"));
            Assert.Equal("wall-a", holder.Draft.Room.TerrainAt(new Cell(2, 2)).EntityId);
        }

        [Fact]
        public void WallOverTheSameWallIsNoChangeAndKeepsItsEntity()
        {
            var draft = BrushFixture.Draft();
            draft.Room.SetTerrain(new Cell(2, 2), TerrainCategories.Wall, "wall-a");
            var plan = BrushPlanner.Preflight(draft, new[] { new Cell(2, 2) }, BrushTool.Wall);
            Assert.Null(plan.Error);
            Assert.True(plan.NoChange);
            Assert.Equal("wall-a", draft.Room.TerrainAt(new Cell(2, 2)).EntityId);
        }
    }
}
