using System;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RecoveryFloorPlacementTests
    {
        [Theory]
        [InlineData(BrushTool.Floor)]
        [InlineData(BrushTool.Wall)]
        [InlineData(BrushTool.Erase)]
        [InlineData(BrushTool.Openable)]
        public void StrokeOverExternalRecoveryFloorIsRejectedWithoutMutation(BrushTool tool)
        {
            var draft = BrushFixture.Draft();
            var floor = new Cell(-1, 2);
            draft.Room.RecoveryDeviceFloor = floor;
            var holder = new DraftHolder(draft);
            var plan = BrushPlanner.Preflight(draft, new[] { new Cell(2, 2), floor }, tool);
            Assert.Contains("恢复装置", plan.Error);
            Assert.Throws<InvalidOperationException>(() => new BrushCommand(holder, new[] { new Cell(2, 2), floor }, tool).Apply());
            Assert.Same(draft, holder.Draft);
            Assert.Equal(7, draft.Room.Width);
        }

        [Fact]
        public void ObjectAndDoorPlacementRejectRecoveryFloor()
        {
            var draft = BrushFixture.Draft();
            var floor = new Cell(-1, 2);
            draft.Room.RecoveryDeviceFloor = floor;
            Assert.Contains("恢复装置", PlacementRules.Error(draft, ObjectCategories.Box, floor, null));
            Assert.Contains("恢复装置", PlacementRules.Error(draft, ObjectCategories.Custom, floor, null));
            Assert.Null(DoorPlacement.Outward(draft.Room, floor));
            Assert.Throws<InvalidOperationException>(() => new PlaceObjectCommand(draft, "new-box", ObjectCategories.Box, floor).Apply());
        }
    }
}
