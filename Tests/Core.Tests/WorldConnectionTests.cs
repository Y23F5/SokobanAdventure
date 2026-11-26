using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class WorldConnectionTests
    {
        static DoorInfo Door(string id, int x, int y, int ox, int oy, string role) =>
            new DoorInfo(id, new Cell(x, y), new Cell(ox, oy), role);

        static WorldRoomInfo RoomA() => new WorldRoomInfo("a", "spawn", new[] { Door("exit", 4, 2, 1, 0, "exit") });

        static WorldRoomInfo RoomB() => new WorldRoomInfo("b", "spawn", new[] { Door("entry", 0, 2, -1, 0, "entry") });

        static Dictionary<string, WorldRoomInfo> Rooms(params WorldRoomInfo[] rooms) => rooms.ToDictionary(r => r.LevelId);

        static WorldRoomRef Ref(string id, int x, int y) => new WorldRoomRef(id, id + ".json", new Cell(x, y), new string[0]);

        static WorldConnection Link(string id, string mode, string fromRoom, string fromDoor, string toRoom, string toDoor) =>
            new WorldConnection(id, mode, new WorldEndpoint(fromRoom, fromDoor), new WorldEndpoint(toRoom, toDoor), false);

        static WorldDocument World(WorldRoomRef[] rooms, WorldConnection[] links, WorldEndpoint start = null, WorldEndpoint finish = null) =>
            new WorldDocument("w", 1, new Cell(0, 0), new Cell(19, 9), rooms, links,
                start ?? new WorldEndpoint("a", "spawn"), finish ?? new WorldEndpoint("a", "exit"), new[] { "a" });

        static WorldRoomRef[] Both(int bx) => new[] { Ref("a", 0, 0), Ref("b", bx, 0) };

        static WorldConnection Direct() => Link("c1", "direct", "a", "exit", "b", "entry");

        [Fact]
        public void AdjacentOpposedDirectConnectionHasNoIssue()
        {
            var world = World(Both(5), new[] { Direct() });
            Assert.Empty(WorldValidator.Connections(world, Rooms(RoomA(), RoomB())));
        }

        [Fact]
        public void MissingDoorReportsEndpointMissing()
        {
            var world = World(Both(5), new[] { Link("c1", "teleport", "a", "nope", "b", "entry") });
            Assert.Equal(new[] { "c1 a.nope → b.entry 端点缺失" }, WorldValidator.Connections(world, Rooms(RoomA(), RoomB())).ToArray());
        }

        [Fact]
        public void UnplacedRoomReportsEndpointMissing()
        {
            var world = World(new[] { Ref("a", 0, 0) }, new[] { Direct() });
            Assert.Contains("c1 a.exit → b.entry 端点缺失", WorldValidator.Connections(world, Rooms(RoomA(), RoomB())));
        }

        [Fact]
        public void GapBetweenDirectDoorsIsReported()
        {
            var world = World(Both(6), new[] { Direct() });
            Assert.Equal(new[] { "c1 a.exit → b.entry 直连门不相邻或朝向不匹配" }, WorldValidator.Connections(world, Rooms(RoomA(), RoomB())).ToArray());
        }

        [Fact]
        public void SameFacingDirectDoorsAreReported()
        {
            var other = new WorldRoomInfo("b", "spawn", new[] { Door("entry", 4, 2, 1, 0, "entry") });
            var world = World(Both(1), new[] { Direct() });
            Assert.Contains("直连门不相邻或朝向不匹配", string.Concat(WorldValidator.Connections(world, Rooms(RoomA(), other))));
        }

        [Fact]
        public void TeleportIgnoresGeometry()
        {
            var world = World(Both(12), new[] { Link("c1", "teleport", "a", "exit", "b", "entry") });
            Assert.Empty(WorldValidator.Connections(world, Rooms(RoomA(), RoomB())));
        }

        [Fact]
        public void UnknownModeIsReported()
        {
            var world = World(Both(5), new[] { Link("c1", "warp", "a", "exit", "b", "entry") });
            Assert.Equal(new[] { "c1 a.exit → b.entry 连接方式无效" }, WorldValidator.Connections(world, Rooms(RoomA(), RoomB())).ToArray());
        }

        [Fact]
        public void DuplicateAndBlankConnectionIdsAreReported()
        {
            var rooms = Rooms(RoomA(), RoomB());
            var twice = World(Both(5), new[] { Direct(), Direct() });
            Assert.Contains("连接 ID 缺失或重复 c1", WorldValidator.Connections(twice, rooms));
            var blank = World(Both(5), new[] { Link(" ", "teleport", "a", "exit", "b", "entry") });
            Assert.Contains("连接 ID 缺失或重复  ", WorldValidator.Connections(blank, rooms));
        }

        [Fact]
        public void DoorUsedByTwoConnectionsIsReported()
        {
            var world = World(Both(5), new[] { Direct(), Link("c2", "teleport", "a", "exit", "b", "entry") });
            var issues = WorldValidator.Connections(world, Rooms(RoomA(), RoomB())).ToArray();
            Assert.Contains("c2 端点重复使用 (a, exit)", issues);
            Assert.Contains("c2 端点重复使用 (b, entry)", issues);
        }

        [Fact]
        public void MovingRoomUsesCandidateOriginEvenWhenPlacedElsewhere()
        {
            var world = World(Both(12), new[] { Direct() });
            var rooms = Rooms(RoomA(), RoomB());
            Assert.Empty(WorldValidator.Connections(world, rooms, "b", new Cell(5, 0)));
            Assert.Contains("c1 a.exit → b.entry 直连门不相邻或朝向不匹配", WorldValidator.Connections(world, rooms, "b", new Cell(9, 0)));
        }

        [Fact]
        public void ValidStartAndFinishHaveNoIssue()
        {
            var world = World(Both(5), new WorldConnection[0]);
            Assert.Empty(WorldValidator.Endpoints(world, Rooms(RoomA(), RoomB())));
        }

        [Fact]
        public void StartWithUnknownSpawnIsReported()
        {
            var world = World(Both(5), new WorldConnection[0], new WorldEndpoint("a", "other"));
            Assert.Equal(new[] { "世界起点无效 a" }, WorldValidator.Endpoints(world, Rooms(RoomA(), RoomB())).ToArray());
        }

        [Fact]
        public void StartInUnplacedRoomIsReported()
        {
            var world = World(new[] { Ref("a", 0, 0) }, new WorldConnection[0], new WorldEndpoint("b", "spawn"));
            Assert.Contains("世界起点无效 b", WorldValidator.Endpoints(world, Rooms(RoomA(), RoomB())));
        }

        [Fact]
        public void FinishWithMissingDoorIsReported()
        {
            var world = World(Both(5), new WorldConnection[0], null, new WorldEndpoint("a", "gone"));
            var issue = Assert.Single(WorldValidator.Endpoints(world, Rooms(RoomA(), RoomB())));
            Assert.StartsWith("世界终点门无效 ", issue);
            Assert.Contains("gone", issue);
        }
    }
}
