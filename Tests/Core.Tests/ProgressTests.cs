using System;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class ProgressTests
    {
        static RecoveryDevice Item(string id) => new RecoveryDevice(id, RecoveryDeviceKind.Forklift, new Cell(-1, 1), 1, "script", 3.7f);

        static ItemBag Bag(params string[] ids)
        {
            var bag = new ItemBag();
            foreach (var id in ids) bag.Grant(id);
            return bag;
        }

        [Fact]
        public void FirstVisitIsReportedOnce()
        {
            var set = new ExplorationSet();
            Assert.True(set.Visit("a"));
            Assert.False(set.Visit("a"));
            Assert.True(set.Contains("a"));
            Assert.False(set.Contains("b"));
        }

        [Fact]
        public void GrantSelectsAndMarksNewItemPending()
        {
            var bag = Bag("a");
            Assert.Equal(new[] { "a" }, bag.Items);
            Assert.Equal("a", bag.Selected);
            Assert.Equal("a", bag.Pending);
        }

        [Fact]
        public void DuplicateGrantIsIgnoredAndDoesNotReopenNotice()
        {
            var bag = Bag("a");
            bag.Accept();
            Assert.False(bag.Grant("a"));
            Assert.Single(bag.Items);
            Assert.Null(bag.Pending);
        }

        [Fact]
        public void AcceptClearsPendingOnly()
        {
            var bag = Bag("a", "b");
            bag.Accept();
            Assert.Null(bag.Pending);
            Assert.Equal("b", bag.Selected);
            Assert.Equal(2, bag.Items.Count);
        }

        [Fact]
        public void CycleWrapsInBothDirections()
        {
            var bag = Bag("a", "b", "c");
            bag.Cycle(1);
            Assert.Equal("a", bag.Selected);
            bag.Cycle(-1);
            Assert.Equal("c", bag.Selected);
            bag.Cycle(-1);
            Assert.Equal("b", bag.Selected);
        }

        [Fact]
        public void CycleMovesOneStepWhateverTheMagnitude()
        {
            var bag = Bag("a", "b", "c");
            bag.Cycle(5);
            Assert.Equal("a", bag.Selected);
        }

        [Fact]
        public void CycleOnEmptyBagDoesNothing()
        {
            var bag = new ItemBag();
            bag.Cycle(1);
            Assert.Null(bag.Selected);
        }

        [Fact]
        public void EnteringRoomWithItemExploresAndGrantsOnce()
        {
            var p = new PlayProgress();
            Assert.True(p.Enter("warehouse", Item("recovery-1")));
            Assert.Equal(new[] { "warehouse" }, p.Bag.Items);
            Assert.Equal("warehouse", p.Bag.Pending);
            p.Bag.Accept();
            Assert.False(p.Enter("warehouse", Item("recovery-1")));
            Assert.Null(p.Bag.Pending);
        }

        [Fact]
        public void ExplorationAndBagAreIndependent()
        {
            var p = new PlayProgress();
            p.Enter("hall", null);
            Assert.True(p.Exploration.Contains("hall"));
            Assert.Empty(p.Bag.Items);
            Assert.Null(p.Bag.Selected);
        }

        [Fact]
        public void RestoreAcceptsConsistentData()
        {
            var bag = ItemBag.Restore(new[] { "a", "b" }, "a", "b");
            Assert.Equal("a", bag.Selected);
            Assert.Equal("b", bag.Pending);
            Assert.Empty(ItemBag.Restore(new string[0], null, null).Items);
        }

        [Fact]
        public void RestoreRejectsInconsistentData()
        {
            Assert.Throws<FormatException>(() => ItemBag.Restore(new[] { "a", "a" }, "a", null));
            Assert.Throws<FormatException>(() => ItemBag.Restore(new[] { "a" }, "b", null));
            Assert.Throws<FormatException>(() => ItemBag.Restore(new[] { "a" }, "a", "b"));
            Assert.Throws<FormatException>(() => ItemBag.Restore(new[] { "a" }, null, null));
        }

        [Fact]
        public void ExplorationRestoreRejectsDuplicates()
        {
            Assert.Throws<FormatException>(() => ExplorationSet.Restore(new[] { "a", "a" }));
            Assert.True(ExplorationSet.Restore(new[] { "a", "b" }).Contains("b"));
        }
    }
}
