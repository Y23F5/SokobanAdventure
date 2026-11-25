using System.Collections.Generic;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class LogTests
    {
        static List<string> Capture(System.Action action)
        {
            var lines = new List<string>();
            var oldSink = Log.Sink;
            Log.Sink = (category, message) => lines.Add(category + ":" + message);
            try { action(); }
            finally { Log.Sink = oldSink; Log.Enabled = true; Log.Unmute("a"); Log.Unmute("b"); }
            return lines;
        }

        [Fact]
        public void WriteReachesSinkWhenEnabled()
        {
            var lines = Capture(() => Log.Write("a", "hello"));
            Assert.Equal(new[] { "a:hello" }, lines);
        }

        [Fact]
        public void DisabledSwitchSilencesEverything()
        {
            var lines = Capture(() => { Log.Enabled = false; Log.Write("a", "x"); Log.Write("b", "y"); });
            Assert.Empty(lines);
        }

        [Fact]
        public void MutedCategoryIsSkippedOthersStay()
        {
            var lines = Capture(() => { Log.Mute("a"); Log.Write("a", "x"); Log.Write("b", "y"); });
            Assert.Equal(new[] { "b:y" }, lines);
        }
    }
}
