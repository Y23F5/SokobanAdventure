using System.IO;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class FactoryBeltScriptTests
    {
        static LuaRecoveryMethod Method()
        {
            var host = new LuaHost();
            host.Register(LuaRecoveryMethod.CreateEntry());
            string source = File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "RecoveryScripts", "factory-belt.lua.txt"));
            return new LuaRecoveryMethod(host, source);
        }

        static RecoveryContext Ctx(Cell from, Cell home, Cell? anchor = null, int rotation = 1)
        {
            var room = new Room("factory", 10, 7);
            var device = new RecoveryDevice("arm", RecoveryDeviceKind.Arm, anchor ?? new Cell(-1, 1), rotation, "factory-belt", 3.7f);
            return new RecoveryContext("factory", "box-1", RecoveryRules.Route(from, home), 3.7f,
                device: device, wallInward: RecoveryDeviceRules.WallInward(room, device.Anchor));
        }

        [Fact]
        public void CargoStartsAtSource()
        {
            var cargo = Method().Pose(0f, Ctx(new Cell(5, 4), new Cell(4, 2))).Single(p => p.PartId == "box-1");
            Assert.Equal(5f, cargo.X, 3);
            Assert.Equal(4f, cargo.Z, 3);
        }

        [Fact]
        public void CargoEndsAtHomeOnTheGround()
        {
            var cargo = Method().Pose(1f, Ctx(new Cell(5, 4), new Cell(4, 2))).Single(p => p.PartId == "box-1");
            Assert.Equal(4f, cargo.X, 3);
            Assert.Equal(2f, cargo.Z, 3);
            Assert.Equal(0f, cargo.Y, 3);
        }

        [Fact]
        public void RollerCountGrowsWithDistance()
        {
            var near = Method().Pose(0.5f, Ctx(new Cell(5, 1), new Cell(4, 1))).Count(p => p.PartId.StartsWith("roller-"));
            var far = Method().Pose(0.5f, Ctx(new Cell(9, 1), new Cell(2, 1))).Count(p => p.PartId.StartsWith("roller-"));
            Assert.True(far > near);
        }

        [Fact]
        public void BeltIsRetractedAtStartAndEnd()
        {
            var m = Method();
            var c = Ctx(new Cell(5, 4), new Cell(4, 2));
            Assert.False(m.Pose(0f, c).Single(p => p.PartId == "belt").Visible);
            Assert.False(m.Pose(1f, c).Single(p => p.PartId == "belt").Visible);
            Assert.True(m.Pose(0.5f, c).Single(p => p.PartId == "belt").Visible);
        }

        [Theory]
        [InlineData(-1, 2, 1)]
        [InlineData(10, 2, 3)]
        [InlineData(2, -1, 0)]
        [InlineData(2, 7, 2)]
        public void ExtendedBeltConnectsOutsideArmAndCargo(int x, int z, int rotation)
        {
            var ctx = Ctx(new Cell(5, 4), new Cell(4, 2), new Cell(x, z), rotation);
            var parts = Method().Pose(.33f, ctx);
            var mount = parts.Single(p => p.PartId == "mount");
            var belt = parts.Single(p => p.PartId == "belt");
            var grabber = parts.Single(p => p.PartId == "grabber");
            var inward = ctx.WallInward.Value;
            Assert.Equal(-.25f, (mount.X - x) * inward.X + (mount.Z - z) * inward.Y, 3);
            var cargo = parts.Single(p => p.PartId == "box-1");
            Assert.Equal(cargo.X, grabber.X, 3);
            Assert.Equal(cargo.Z, grabber.Z, 3);
            Assert.True(cargo.Y > belt.Y + .1f);
            Assert.InRange(belt.ScaleX, 2f, 9f);
            var yaw = belt.Yaw * System.Math.PI / 180;
            Assert.Equal(mount.X, belt.X - (float)System.Math.Cos(yaw) * belt.ScaleX / 2, 3);
            Assert.Equal(mount.Z, belt.Z + (float)System.Math.Sin(yaw) * belt.ScaleX / 2, 3);
        }

        [Fact]
        public void ZeroDistanceDoesNotFail()
        {
            var parts = Method().Pose(0.5f, Ctx(new Cell(3, 3), new Cell(3, 3)));
            Assert.NotEmpty(parts);
        }
    }
}
