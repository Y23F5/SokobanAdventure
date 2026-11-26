using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RecoveryMappingTests
    {
        static RoomDraftModel Room(bool wallBesideExit = true)
        {
            var room = new Room("r", 11, 9);
            for (int x = 0; x < 11; x++)
                for (int y = 0; y < 9; y++)
                    room.SetTerrain(new Cell(x, y), x == 0 || y == 0 || x == 10 || y == 8 ? TerrainCategories.Wall : TerrainCategories.Floor);
            room.SetTerrain(new Cell(0, 3), TerrainCategories.Door, "entry");
            room.SetTerrain(new Cell(10, 3), TerrainCategories.Door, "exit");
            if (!wallBesideExit)
            {
                room.SetTerrain(new Cell(10, 4), TerrainCategories.Floor);
                room.SetTerrain(new Cell(10, 2), TerrainCategories.Floor);
            }
            var doors = new[]
            {
                new DoorInfo("entry", new Cell(0, 3), Cell.Left, "entry"),
                new DoorInfo("exit", new Cell(10, 3), Cell.Right, "exit"),
            };
            return new RoomDraftModel(room, doors, "entry");
        }

        [Fact]
        public void WarehouseMapsToLiftOnly()
        {
            var items = RecoveryMapping.FromPresentation("warehouse", Room());
            Assert.Equal(new[] { "warehouse-lift" }, items.Select(i => i.ScriptRef).ToArray());
        }

        [Fact]
        public void FactoryMapsToBeltOnly()
        {
            var items = RecoveryMapping.FromPresentation("factory", Room());
            Assert.Equal(new[] { "factory-belt" }, items.Select(i => i.ScriptRef).ToArray());
        }

        [Fact]
        public void ForestMapsToDecayAndVineWithGateParams()
        {
            var items = RecoveryMapping.FromPresentation("forest", Room());
            Assert.Equal(new[] { "forest-decay", "forest-vine" }, items.Select(i => i.ScriptRef).ToArray());
            var vine = items.Single(i => i.ScriptRef == "forest-vine");
            Assert.Equal(10f - 0.70f, vine.Params["gateX"], 3);
            Assert.Equal(4f, vine.Params["gateZ"], 3);
            Assert.Equal(-90f, vine.Params["yaw"], 3);
        }

        [Fact]
        public void ForestWithoutWallBesideExitHasNoVine()
        {
            var items = RecoveryMapping.FromPresentation("forest", Room(false));
            Assert.Equal(new[] { "forest-decay" }, items.Select(i => i.ScriptRef).ToArray());
        }

        [Fact]
        public void ItemIdsAreUniqueAndPositiveDuration()
        {
            var items = RecoveryMapping.FromPresentation("forest", Room());
            Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
            Assert.All(items, i => Assert.True(i.Duration > 0));
        }

        [Fact]
        public void UnknownPresentationYieldsNothing()
        {
            Assert.Empty(RecoveryMapping.FromPresentation("nope", Room()));
        }
    }
}
