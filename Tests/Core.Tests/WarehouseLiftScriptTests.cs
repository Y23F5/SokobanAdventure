using System.IO;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class WarehouseLiftScriptTests
    {
        static LuaRecoveryMethod Method()
        {
            var host = new LuaHost();
            host.Register(LuaRecoveryMethod.CreateEntry());
            string source = File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "RecoveryScripts", "warehouse-lift.lua.txt"));
            return new LuaRecoveryMethod(host, source);
        }

        static RecoveryContext Ctx() =>
            new RecoveryContext("warehouse", "box-1", RecoveryRules.Route(new Cell(4, 2), new Cell(3, 2)), 3.7f);

        [Fact]
        public void CargoStartsAtSourceOnTheGround()
        {
            var cargo = Method().Pose(0f, Ctx()).Single(p => p.PartId == "box-1");
            Assert.Equal(4f, cargo.X, 3);
            Assert.Equal(0f, cargo.Y, 3);
        }

        [Fact]
        public void CargoIsLiftedWhileCarried()
        {
            var cargo = Method().Pose(0.55f, Ctx()).Single(p => p.PartId == "box-1");
            Assert.True(cargo.Y > 1f);
            Assert.InRange(cargo.X, 3f, 4f);
        }

        [Fact]
        public void CargoEndsAtHomeCellOnTheGround()
        {
            var cargo = Method().Pose(1f, Ctx()).Single(p => p.PartId == "box-1");
            Assert.Equal(3f, cargo.X, 3);
            Assert.Equal(0f, cargo.Y, 3);
        }

        [Fact]
        public void TruckReturnsToParkingAtTheEnd()
        {
            var parts = Method().Pose(1f, Ctx());
            Assert.Equal(-1.2f, parts.Single(p => p.PartId == "truck").X, 3);
            Assert.Equal(6, parts.Count);
        }

        [Fact]
        public void PoseIsPure()
        {
            var a = Method().Pose(0.4f, Ctx()).Single(p => p.PartId == "fork");
            var b = Method().Pose(0.4f, Ctx()).Single(p => p.PartId == "fork");
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
        }
    }
}
