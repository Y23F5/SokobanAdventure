using System.Collections.Generic;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class UiModesTests
    {
        [Fact]
        public void StartsInLayoutAndBackWithEmptyStackIsNoOp()
        {
            var m = new UiModeMachine();
            Assert.Equal(UiMode.Layout, m.Current);
            Assert.False(m.Back());
            Assert.Equal(UiMode.Layout, m.Current);
        }

        [Fact]
        public void CheckThenBackReturnsToLayoutInOneStep()
        {
            var m = new UiModeMachine();
            Assert.True(m.Enter(UiMode.Check));
            Assert.True(m.Back());
            Assert.Equal(UiMode.Layout, m.Current);
            Assert.False(m.Back());
        }

        [Fact]
        public void BackReturnsToPreviousModeNotAlwaysLayout()
        {
            var m = new UiModeMachine();
            m.Enter(UiMode.StandaloneEdit);
            m.Enter(UiMode.Check);
            m.Back();
            Assert.Equal(UiMode.StandaloneEdit, m.Current);
            m.Back();
            Assert.Equal(UiMode.Layout, m.Current);
        }

        [Fact]
        public void EnteringSameModeIsIgnored()
        {
            var m = new UiModeMachine();
            m.Enter(UiMode.Check);
            Assert.False(m.Enter(UiMode.Check));
            m.Back();
            Assert.Equal(UiMode.Layout, m.Current);
        }

        [Fact]
        public void TrialCannotStartFromCheck()
        {
            var m = new UiModeMachine();
            m.Enter(UiMode.Check);
            Assert.False(m.Enter(UiMode.Trial));
            Assert.Equal(UiMode.Check, m.Current);
        }

        [Fact]
        public void MapInputIsBlockedOnlyInCheckAndTrial()
        {
            var m = new UiModeMachine();
            Assert.False(m.BlocksMapInput);
            m.Enter(UiMode.Trial);
            Assert.True(m.BlocksMapInput);
            m.Back();
            m.Enter(UiMode.StandaloneEdit);
            Assert.False(m.BlocksMapInput);
            m.Enter(UiMode.Check);
            Assert.True(m.BlocksMapInput);
        }

        [Fact]
        public void ChangedEventReportsOldAndNew()
        {
            var m = new UiModeMachine();
            var seen = new List<(UiMode, UiMode)>();
            m.Changed += (o, n) => seen.Add((o, n));
            m.Enter(UiMode.Trial);
            m.Back();
            Assert.Equal(new[] { (UiMode.Layout, UiMode.Trial), (UiMode.Trial, UiMode.Layout) }, seen);
        }
    }
}
