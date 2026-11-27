/**
 * @file WarehouseLiftScriptTests.cs
 * @brief 驱动仓库 Lua 演出并验证车身沿墙、货叉伸缩和箱子落位。
 * Method()：加载实际发布的读取装置坐标的 Lua 脚本。
 * Ctx(anchor, rotation)：构造归位路线、装置朝向与实际相邻墙的法线。
 * CargoStartsAtSourceOnTheGround()、CargoIsLiftedWhileCarried()、CargoEndsAtHomeCellOnTheGround()：验证箱子起点、中段高度和归位。
 * TruckTracksOutsideWall()：逐帧检查四侧车身固定于墙外、末端伸入房间并返回停放点。
 * PoseIsPure()：相同输入生成同样零件位置。
 */
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

        static RecoveryContext Ctx(Cell? anchor = null, int rotation = 1)
        {
            var room = new Room("warehouse", 9, 7);
            var device = new RecoveryDevice("truck", RecoveryDeviceKind.Forklift, anchor ?? new Cell(-1, 1), rotation,
                "warehouse-lift", 3.7f);
            return new RecoveryContext("warehouse", "box-1", RecoveryRules.Route(new Cell(4, 2), new Cell(3, 2)),
                3.7f, device: device, wallInward: RecoveryDeviceRules.WallInward(room, device.Anchor));
        }

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

        [Theory]
        [InlineData(-1, 1, 1)]
        [InlineData(9, 1, 3)]
        [InlineData(1, -1, 0)]
        [InlineData(1, 7, 2)]
        [InlineData(-1, 1, 0)]
        public void TruckTracksOutsideWall(int anchorX, int anchorY, int rotation)
        {
            var ctx = Ctx(new Cell(anchorX, anchorY), rotation);
            var inward = ctx.WallInward.Value;
            float Depth(RecoveryPart p) => (p.X - anchorX) * inward.X + (p.Z - anchorY) * inward.Y;
            var method = Method();
            var start = method.Pose(0, ctx).Single(p => p.PartId == "truck");
            for (int sample = 0; sample <= 100; sample++)
            {
                var frame = method.Pose(sample / 100f, ctx);
                Assert.Equal(7, frame.Count);
                Assert.Equal(-.26f, Depth(frame.Single(p => p.PartId == "truck")), 3);
            }
            var middle = method.Pose(.55f, ctx);
            var truck = middle.Single(p => p.PartId == "truck");
            Assert.True(truck.X != start.X || truck.Z != start.Z);
            Assert.Equal(rotation * 90f, truck.Yaw, 3);
            Assert.True(Depth(middle.Single(p => p.PartId == "fork")) > 1f);
            Assert.Equal(2.74f, middle.Single(p => p.PartId == "boom").Y, 3);
            var end = method.Pose(1, ctx).Single(p => p.PartId == "truck");
            Assert.Equal(start.X, end.X, 3);
            Assert.Equal(start.Z, end.Z, 3);
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
