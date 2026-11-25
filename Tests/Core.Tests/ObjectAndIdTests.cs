using System;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class ObjectAndIdTests
    {
        [Fact]
        public void OnlyBoxIsPushable()
        {
            var registry = ObjectCategories.CreateDefault();
            foreach (var id in registry.Ids)
                Assert.Equal(id == ObjectCategories.Box, registry.Get(id).Pushable);
        }

        [Fact]
        public void BoxAndTargetMatchTargets()
        {
            var registry = ObjectCategories.CreateDefault();
            Assert.True(registry.Get(ObjectCategories.Box).MatchesTarget);
            Assert.True(registry.Get(ObjectCategories.Target).MatchesTarget);
            Assert.False(registry.Get(ObjectCategories.Spawn).MatchesTarget);
            Assert.False(registry.Get(ObjectCategories.Custom).MatchesTarget);
        }

        [Fact]
        public void SpawnIsExactlyOneAndTargetAtLeastOne()
        {
            var registry = ObjectCategories.CreateDefault();
            var spawn = registry.Get(ObjectCategories.Spawn);
            Assert.Equal(1, spawn.MinCount);
            Assert.Equal(1, spawn.MaxCount);
            Assert.Equal(1, registry.Get(ObjectCategories.Target).MinCount);
        }

        [Fact]
        public void UnknownObjectCategoryThrows()
        {
            Assert.Throws<UnknownIdException>(() => ObjectCategories.CreateDefault().Get("decoration"));
        }

        [Fact]
        public void IdSpaceRejectsDuplicateAndEmpty()
        {
            var ids = new IdSpace("warehouse");
            ids.Add("box-1", ObjectCategories.Box);
            Assert.Throws<ArgumentException>(() => ids.Add("box-1", ObjectCategories.Target));
            Assert.Throws<ArgumentException>(() => ids.Add("", ObjectCategories.Box));
        }

        [Fact]
        public void IdSpaceRemoveAllowsReuseAndKeepsOrder()
        {
            var ids = new IdSpace("warehouse");
            ids.Add("a", "box");
            ids.Add("b", "target");
            Assert.True(ids.Remove("a"));
            Assert.False(ids.Remove("a"));
            ids.Add("a", "box");
            Assert.Equal(new[] { "b", "a" }, ids.Ids);
            Assert.Equal("box", ids.KindOf("a"));
        }

        [Fact]
        public void IdSpaceUnknownKindOfThrows()
        {
            var ids = new IdSpace("forest");
            Assert.Throws<UnknownIdException>(() => ids.KindOf("missing"));
        }
    }
}
