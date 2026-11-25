using System;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RoomTests
    {
        [Fact]
        public void NewRoomIsAllVoid()
        {
            var room = new Room("r", 3, 2);
            for (int x = 0; x < 3; x++)
                for (int y = 0; y < 2; y++)
                    Assert.Equal(TerrainCategories.Void, room.TerrainAt(new Cell(x, y)).CategoryId);
            Assert.Empty(room.Objects);
        }

        [Fact]
        public void OutOfRangeReadsAsVoidAndWriteThrows()
        {
            var room = new Room("r", 2, 2);
            Assert.False(room.Inside(new Cell(2, 0)));
            Assert.Equal(TerrainCategories.Void, room.TerrainAt(new Cell(-1, 0)).CategoryId);
            Assert.Throws<ArgumentOutOfRangeException>(() => room.SetTerrain(new Cell(5, 5), TerrainCategories.Floor));
        }

        [Fact]
        public void TerrainEntityIdSharesIdSpaceWithObjects()
        {
            var room = new Room("r", 3, 3);
            room.SetTerrain(new Cell(0, 0), TerrainCategories.Wall, "wall-a");
            Assert.True(room.Ids.Contains("wall-a"));
            Assert.Throws<ArgumentException>(() => room.AddObject("wall-a", ObjectCategories.Box, new Cell(1, 1)));
        }

        [Fact]
        public void OverwritingTerrainReleasesOldEntityId()
        {
            var room = new Room("r", 3, 3);
            room.SetTerrain(new Cell(0, 0), TerrainCategories.Wall, "wall-a");
            room.SetTerrain(new Cell(0, 0), TerrainCategories.Floor);
            Assert.False(room.Ids.Contains("wall-a"));
        }

        [Fact]
        public void ObjectAddQueryRemove()
        {
            var room = new Room("r", 4, 4);
            room.AddObject("box-1", ObjectCategories.Box, new Cell(1, 1));
            room.AddObject("target-1", ObjectCategories.Target, new Cell(2, 1));
            Assert.Equal("box-1", room.ObjectAt(new Cell(1, 1)).Id);
            Assert.Single(room.ObjectsOf(ObjectCategories.Target));
            Assert.True(room.RemoveObject("box-1"));
            Assert.False(room.RemoveObject("box-1"));
            Assert.Null(room.ObjectAt(new Cell(1, 1)));
            Assert.False(room.Ids.Contains("box-1"));
        }

        [Fact]
        public void DuplicateObjectIdIsRejected()
        {
            var room = new Room("r", 4, 4);
            room.AddObject("a", ObjectCategories.Box, new Cell(0, 0));
            Assert.Throws<ArgumentException>(() => room.AddObject("a", ObjectCategories.Target, new Cell(1, 0)));
            Assert.Equal(1, room.Objects.Count(o => o.Id == "a"));
        }
    }
}
