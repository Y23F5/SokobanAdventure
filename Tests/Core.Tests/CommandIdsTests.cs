using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class CommandIdsTests
    {
        [Fact]
        public void UnknownCommandThrowsInsteadOfFallingThrough()
        {
            var registry = CommandIds.CreateDefault();
            Assert.Throws<UnknownIdException>(() => registry.Get("试玩"));
        }

        [Fact]
        public void EveryIdIsDomainDotActionAndUnique()
        {
            var registry = CommandIds.CreateDefault();
            Assert.Equal(registry.Ids.Count, registry.Ids.Distinct().Count());
            foreach (var id in registry.Ids)
            {
                Assert.Contains(".", id);
                Assert.Equal(CommandIds.Domain(id), registry.Get(id).Domain);
            }
        }

        [Fact]
        public void ToolAndGridCommandsDoNotChangeModel()
        {
            var registry = CommandIds.CreateDefault();
            foreach (var id in new[] { CommandIds.ToolSelect, CommandIds.ToolMove, CommandIds.ToolErase, CommandIds.GridToggle, CommandIds.WorldPublish })
                Assert.False(registry.Get(id).ChangesModel);
        }

        [Fact]
        public void EditingCommandsChangeModel()
        {
            var registry = CommandIds.CreateDefault();
            foreach (var id in new[] { CommandIds.TerrainPaint, CommandIds.ObjectPlace, CommandIds.RecoveryAddItem, CommandIds.WorldMoveRoom })
                Assert.True(registry.Get(id).ChangesModel);
        }

        [Fact]
        public void StandaloneEditCommandsExist()
        {
            var registry = CommandIds.CreateDefault();
            Assert.True(registry.Contains("room.enterStandaloneEdit"));
            Assert.True(registry.Contains("room.returnToWorld"));
        }
    }
}
