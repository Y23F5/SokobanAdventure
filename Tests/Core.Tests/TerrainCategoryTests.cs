using System;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class TerrainCategoryTests
    {
        [Fact]
        public void UnknownIdThrowsInsteadOfPassing()
        {
            var registry = TerrainCategories.CreateDefault();
            var error = Assert.Throws<UnknownIdException>(() => registry.Get("lava"));
            Assert.Equal("lava", error.Id);
        }

        [Fact]
        public void DuplicateRegistrationIsRejected()
        {
            var registry = TerrainCategories.CreateDefault();
            Assert.Throws<ArgumentException>(() =>
                registry.Register(TerrainCategories.Floor, new TerrainCategory("floor", true, true, "floor")));
        }

        [Fact]
        public void IdsKeepRegistrationOrder()
        {
            var registry = TerrainCategories.CreateDefault();
            Assert.Equal(new[] { "void", "floor", "wall", "door", "openable" }, registry.Ids);
        }

        [Fact]
        public void OnlyFloorCarriesObjects()
        {
            var registry = TerrainCategories.CreateDefault();
            foreach (var id in registry.Ids)
                Assert.Equal(id == TerrainCategories.Floor, registry.Get(id).CarriesObjects);
        }

        [Fact]
        public void WalkabilityFollowsCategory()
        {
            var registry = TerrainCategories.CreateDefault();
            Assert.True(registry.Get(TerrainCategories.Floor).IsWalkable(null));
            Assert.True(registry.Get(TerrainCategories.Door).IsWalkable(null));
            Assert.False(registry.Get(TerrainCategories.Wall).IsWalkable(null));
            Assert.False(registry.Get(TerrainCategories.Void).IsWalkable(null));
        }

        [Fact]
        public void OpenableIsWalkableOnlyWhenOpen()
        {
            var openable = TerrainCategories.CreateDefault().Get(TerrainCategories.Openable);
            Assert.Equal(TerrainCategories.Closed, openable.InitialState);
            Assert.False(openable.IsWalkable(TerrainCategories.Closed));
            Assert.True(openable.IsWalkable(TerrainCategories.Open));
        }
    }
}
