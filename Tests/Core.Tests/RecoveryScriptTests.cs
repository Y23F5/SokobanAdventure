using System.IO;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RecoveryScriptTests
    {
        static string Load(string name) =>
            File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "RecoveryScripts", name + ".lua.txt"));

        static LuaRecoveryMethod Method(string name)
        {
            var host = new LuaHost();
            host.Register(LuaRecoveryMethod.CreateEntry());
            return new LuaRecoveryMethod(host, Load(name));
        }

        static RecoveryContext Ctx() =>
            new RecoveryContext("forest", "box-1", RecoveryRules.Route(new Cell(7, 2), new Cell(6, 2)), 3.7f);

        [Fact]
        public void ForestDecayStartsWithIntactLogsAndNoTree()
        {
            var parts = Method("forest-decay").Pose(0f, Ctx());
            Assert.True(parts.Single(p => p.PartId == "box-1").Visible);
            Assert.False(parts.Single(p => p.PartId == "box-1-decay").Visible);
            Assert.False(parts.Single(p => p.PartId == "box-1-tree").Visible);
        }

        [Fact]
        public void ForestDecayMidwayShowsFragmentsAtOldCell()
        {
            var parts = Method("forest-decay").Pose(0.4f, Ctx());
            var decay = parts.Single(p => p.PartId == "box-1-decay");
            Assert.True(decay.Visible);
            Assert.Equal(7f, decay.X);
            Assert.Equal("ForestDecay", decay.Clip);
            Assert.False(parts.Single(p => p.PartId == "box-1").Visible);
        }

        [Fact]
        public void ForestDecayEndsWithTreeAtHomeCell()
        {
            var parts = Method("forest-decay").Pose(1f, Ctx());
            var tree = parts.Single(p => p.PartId == "box-1-tree");
            Assert.True(tree.Visible);
            Assert.Equal(6f, tree.X);
            Assert.Equal(1f, tree.ClipTime ?? tree.ScaleX);
            Assert.False(parts.Single(p => p.PartId == "box-1-decay").Visible);
        }
    }
}
