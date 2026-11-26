using System;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class WorldStartTests
    {
        static WorldDocument Doc() =>
            new WorldDocument("w", 1, new Cell(0, 0), new Cell(19, 9),
                new[] { new WorldRoomRef("a", "a.json", new Cell(0, 0), new string[0]) },
                new WorldConnection[0],
                new WorldEndpoint("a", "spawn"), new WorldEndpoint("a", "exit"), new[] { "a" });

        [Fact]
        public void EncodeWritesStartAsSpawnId()
        {
            Assert.Contains("\"start\":{\"levelId\":\"a\",\"spawnId\":\"spawn\"}", WorldCodec.Encode(Doc()));
        }

        [Fact]
        public void DecodeReadsSpawnIdStart()
        {
            var back = WorldCodec.Decode(WorldCodec.Encode(Doc()));
            Assert.Equal("a", back.Start.LevelId);
            Assert.Equal("spawn", back.Start.DoorId);
        }

        [Fact]
        public void DecodeStillAcceptsDoorIdStart()
        {
            string text = WorldCodec.Encode(Doc()).Replace("\"spawnId\":\"spawn\"", "\"doorId\":\"spawn\"");
            Assert.Equal("spawn", WorldCodec.Decode(text).Start.DoorId);
        }

        [Fact]
        public void StartWithoutAnyIdIsRejected()
        {
            string text = WorldCodec.Encode(Doc()).Replace("\"spawnId\":\"spawn\"", "\"other\":\"spawn\"");
            Assert.Throws<FormatException>(() => WorldCodec.Decode(text));
        }
    }
}
