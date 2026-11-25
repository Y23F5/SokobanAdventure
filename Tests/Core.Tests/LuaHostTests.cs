using System;
using System.Linq;
using MoonSharp.Interpreter;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class LuaHostTests
    {
        const string Slide = @"
function pose(t, ctx)
  local n = #ctx.route
  local pos = t * (n - 1)
  local i = math.floor(pos)
  local a = ctx.route[i + 1]
  local b = ctx.route[math.min(i + 2, n)]
  local f = pos - i
  return { { id = ctx.boxId, resource = 'box', x = a.x + (b.x - a.x) * f, z = a.y + (b.y - a.y) * f } }
end";

        static LuaHost MakeHost()
        {
            var host = new LuaHost();
            host.Register(LuaRecoveryMethod.CreateEntry());
            return host;
        }

        static RecoveryContext Ctx() =>
            new RecoveryContext("r", "box-1", RecoveryRules.Route(new Cell(4, 1), new Cell(2, 1)), 3.7f);

        [Fact]
        public void PoseReturnsPartsFromScript()
        {
            var method = new LuaRecoveryMethod(MakeHost(), Slide);
            var start = method.Pose(0f, Ctx()).Single();
            var end = method.Pose(1f, Ctx()).Single();
            Assert.Equal("box-1", start.PartId);
            Assert.Equal(4f, start.X, 3);
            Assert.Equal(2f, end.X, 3);
        }

        [Fact]
        public void PoseIsPureSameInputSameOutput()
        {
            var method = new LuaRecoveryMethod(MakeHost(), Slide);
            var a = method.Pose(0.37f, Ctx()).Single();
            var b = method.Pose(0.37f, Ctx()).Single();
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Z, b.Z);
        }

        [Fact]
        public void ScriptIsCompiledOnceAndCached()
        {
            var host = MakeHost();
            var method = new LuaRecoveryMethod(host, Slide);
            method.Pose(0.1f, Ctx());
            method.Pose(0.2f, Ctx());
            method.Pose(0.3f, Ctx());
            Assert.Equal(1, host.CachedCount);
        }

        [Fact]
        public void EnvironmentHasNoTimeRandomOrRuleFunctions()
        {
            foreach (var name in new[] { "os", "io", "setState", "setVisible", "openPassage" })
            {
                var method = new LuaRecoveryMethod(MakeHost(), "function pose(t, ctx) return { { id = 'a', x = (" + name + " ~= nil) and 1 or 0 } } end");
                Assert.Equal(0f, method.Pose(0f, Ctx()).Single().X);
            }
            Assert.Equal(0f, new LuaRecoveryMethod(MakeHost(), "function pose(t, ctx) return { { id = 'a', x = (math.random ~= nil) and 1 or 0 } } end").Pose(0f, Ctx()).Single().X);
        }

        [Fact]
        public void CallingMissingRuleFunctionFails()
        {
            var method = new LuaRecoveryMethod(MakeHost(), "function pose(t, ctx) setState('x') return {} end");
            Assert.ThrowsAny<Exception>(() => method.Pose(0f, Ctx()));
        }

        [Fact]
        public void InfiniteLoopHitsInstructionBudget()
        {
            var method = new LuaRecoveryMethod(MakeHost(), "function pose(t, ctx) while true do end end");
            Assert.Throws<InvalidOperationException>(() => method.Pose(0f, Ctx()));
        }

        [Fact]
        public void OversizedSourceIsRejected()
        {
            var method = new LuaRecoveryMethod(MakeHost(), new string(' ', LuaHost.MaxSourceLength + 1));
            Assert.Throws<InvalidOperationException>(() => method.Pose(0f, Ctx()));
        }

        [Fact]
        public void UnknownEntryThrows()
        {
            var host = new LuaHost();
            Assert.Throws<UnknownIdException>(() => host.Call("behavior.nope", "function f() end", "f", script => new DynValue[0]));
        }

        [Fact]
        public void DuplicateEntryIsRejected()
        {
            var host = MakeHost();
            Assert.Throws<ArgumentException>(() => host.Register(LuaRecoveryMethod.CreateEntry()));
        }

        [Fact]
        public void BadReturnTypeIsRejected()
        {
            var method = new LuaRecoveryMethod(MakeHost(), "function pose(t, ctx) return 5 end");
            Assert.Throws<InvalidOperationException>(() => method.Pose(0f, Ctx()));
        }

        [Fact]
        public void ExtraFieldsMapToPartFields()
        {
            var method = new LuaRecoveryMethod(MakeHost(),
                "function pose(t, ctx) return { { id = 'p', resource = 'vine', x = 1, y = 2, z = 3, yaw = 90, scale = 0.5, color = '#ff0000', visible = false, clip = 1.5 } } end");
            var part = method.Pose(0f, Ctx()).Single();
            Assert.Equal(2f, part.Y);
            Assert.Equal(90f, part.Yaw);
            Assert.Equal(0.5f, part.Scale);
            Assert.Equal("#ff0000", part.Color);
            Assert.False(part.Visible);
            Assert.Equal(1.5f, part.ClipTime);
        }
    }
}
