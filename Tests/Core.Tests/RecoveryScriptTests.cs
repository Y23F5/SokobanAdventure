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

        static RecoveryContext VineCtx() =>
            new RecoveryContext("forest", "box-1", RecoveryRules.Route(new Cell(7, 2), new Cell(6, 2)), 3.7f,
                new System.Collections.Generic.Dictionary<string, float> { { "gateX", 4f }, { "gateZ", 2f }, { "yaw", 90f } });

        [Fact]
        public void ForestVineIsPlacedAtTheGateCell()
        {
            var vine = Method("forest-vine").Pose(0f, VineCtx()).Single();
            Assert.Equal("forest-vine", vine.PartId);
            Assert.Equal("Diorama/ForestRecoveryVine", vine.Resource);
            Assert.Equal(4f, vine.X, 3);
            Assert.Equal(2f, vine.Z, 3);
            Assert.Equal(90f, vine.Yaw, 3);
            Assert.True(vine.Visible);
        }

        [Fact]
        public void ForestVineSamplesRecoveryClipByProgress()
        {
            var method = Method("forest-vine");
            var start = method.Pose(0f, VineCtx()).Single();
            var mid = method.Pose(0.5f, VineCtx()).Single();
            var end = method.Pose(1f, VineCtx()).Single();
            Assert.Equal("VineRecovery", mid.Clip);
            Assert.Equal(0f, start.ClipTime ?? -1f, 3);
            Assert.Equal(0.5f, mid.ClipTime ?? -1f, 3);
            Assert.Equal(1f, end.ClipTime ?? -1f, 3);
        }

        [Fact]
        public void ForestVineWithoutParamsStillPosesAtOrigin()
        {
            var vine = Method("forest-vine").Pose(0.5f, Ctx()).Single();
            Assert.Equal(0f, vine.X, 3);
            Assert.Equal(0f, vine.Z, 3);
        }

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
        public void ForestDeviceDecaysAndRegrowsAtItsAnchor()
        {
            var anchor = new Cell(3, -1);
            var device = new RecoveryDevice("tree", RecoveryDeviceKind.Tree, anchor, 0, "forest-decay", 3.7f);
            var parameters = new System.Collections.Generic.Dictionary<string, float> { ["devicePose"] = 1 };
            var context = new RecoveryContext("forest", device.Id, new[] { anchor }, device.Duration, parameters, device);
            var script = Method("forest-decay");
            for (int step = 0; step <= 100; step++)
            {
                Assert.All(script.Pose(step / 100f, context), part =>
                {
                    Assert.Equal(anchor.X, part.X);
                    Assert.Equal(anchor.Y, part.Z);
                });
            }
            Assert.Equal(1f, script.Pose(0, context).First().Scale);
            Assert.True(script.Pose(.4f, context).Single(part => part.PartId == "stationary-decay").Visible);
            Assert.False(script.Pose(.4f, context).First().Visible);
            Assert.True(script.Pose(.9f, context).First().Visible);
            Assert.Equal(1f, script.Pose(1, context).First().Scale);
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
