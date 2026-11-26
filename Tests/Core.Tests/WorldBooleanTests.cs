using System;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class WorldBooleanTests
    {
        static WorldDocument Doc(bool allowReturn) =>
            new WorldDocument("w", 1, new Cell(0, 0), new Cell(19, 9),
                new[]
                {
                    new WorldRoomRef("a", "a.json", new Cell(0, 0), new string[0]),
                    new WorldRoomRef("b", "b.json", new Cell(6, 0), new string[0]),
                },
                new[] { new WorldConnection("link-1", "direct", new WorldEndpoint("a", "exit"), new WorldEndpoint("b", "entry"), allowReturn) },
                new WorldEndpoint("a", "spawn"), new WorldEndpoint("b", "exit"), new[] { "a" });

        [Fact]
        public void EncodeWritesAllowReturnAsBoolean()
        {
            Assert.Contains("\"allowReturn\":true", WorldCodec.Encode(Doc(true)));
            Assert.Contains("\"allowReturn\":false", WorldCodec.Encode(Doc(false)));
        }

        [Fact]
        public void DecodeAcceptsBooleanAllowReturn()
        {
            Assert.True(WorldCodec.Decode(WorldCodec.Encode(Doc(true))).Connections.Single().AllowReturn);
            Assert.False(WorldCodec.Decode(WorldCodec.Encode(Doc(false))).Connections.Single().AllowReturn);
        }

        [Fact]
        public void DecodeRejectsNonBooleanAllowReturn()
        {
            string text = WorldCodec.Encode(Doc(true)).Replace("\"allowReturn\":true", "\"allowReturn\":\"yes\"");
            Assert.Throws<FormatException>(() => WorldCodec.Decode(text));
        }
    }
}
