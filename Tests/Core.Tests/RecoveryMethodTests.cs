using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RecoveryMethodTests
    {
        static RecoveryContext Ctx() =>
            new RecoveryContext("r", "box-1", RecoveryRules.Route(new Cell(4, 1), new Cell(2, 1)), 3.7f);

        [Fact]
        public void SeedIsStableAndDependsOnRoomAndObject()
        {
            Assert.Equal(RecoveryContext.SeedOf("r", "b"), RecoveryContext.SeedOf("r", "b"));
            Assert.NotEqual(RecoveryContext.SeedOf("r", "b"), RecoveryContext.SeedOf("s", "b"));
            Assert.NotEqual(RecoveryContext.SeedOf("r", "b"), RecoveryContext.SeedOf("r", "c"));
        }

        [Fact]
        public void PoseIsPureSameInputSameOutput()
        {
            var m = new SlideMethod();
            var a = m.Pose(0.37f, Ctx()).Single();
            var b = m.Pose(0.37f, Ctx()).Single();
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Z, b.Z);
        }

        [Fact]
        public void SlideGoesFromCurrentToInitialCell()
        {
            var m = new SlideMethod();
            var start = m.Pose(0f, Ctx()).Single();
            var mid = m.Pose(0.5f, Ctx()).Single();
            var end = m.Pose(1f, Ctx()).Single();
            Assert.Equal(4f, start.X, 3);
            Assert.Equal(3f, mid.X, 3);
            Assert.Equal(2f, end.X, 3);
            Assert.Equal(1f, end.Z, 3);
        }

        [Fact]
        public void ProgressOutsideRangeIsClamped()
        {
            var m = new SlideMethod();
            Assert.Equal(4f, m.Pose(-1f, Ctx()).Single().X, 3);
            Assert.Equal(2f, m.Pose(9f, Ctx()).Single().X, 3);
        }

        [Fact]
        public void PartsInsideZonePassAndOutsideFail()
        {
            var zone = new HashSet<Cell>(RecoveryRules.Route(new Cell(4, 1), new Cell(2, 1)));
            var inside = new[] { new RecoveryPart("a", "box", 3, 0, 1) };
            var outside = new[] { new RecoveryPart("b", "box", 3, 0, 2) };
            Assert.Null(RecoveryPoseCheck.Validate(inside, zone, p => true));
            Assert.NotNull(RecoveryPoseCheck.Validate(outside, zone, p => true));
        }

        [Fact]
        public void PartsOutsideRoomAreNotChecked()
        {
            var zone = new HashSet<Cell>();
            var parts = new[] { new RecoveryPart("truck", "lift", -3, 0, 1) };
            Assert.Null(RecoveryPoseCheck.Validate(parts, zone, p => false));
        }
    }
}
