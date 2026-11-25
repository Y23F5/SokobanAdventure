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

        static RecoveryContext Ctx(Cell from, Cell home) =>
            new RecoveryContext("factory", "box-1", RecoveryRules.Route(from, home), 3.7f);

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
            Assert.True(m.Pose(0f, c).Single(p => p.PartId == "belt").ScaleY < 0.01f);
            Assert.True(m.Pose(1f, c).Single(p => p.PartId == "belt").ScaleY < 0.01f);
            Assert.True(m.Pose(0.5f, c).Single(p => p.PartId == "belt").ScaleY > 0.9f);
        }

        [Fact]
        public void ZeroDistanceDoesNotFail()
        {
            var parts = Method().Pose(0.5f, Ctx(new Cell(3, 3), new Cell(3, 3)));
            Assert.NotEmpty(parts);
        }
    }
}
