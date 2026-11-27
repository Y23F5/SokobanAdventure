using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class WorldCodecTests
    {
        [Fact]
        public void DraftRoundTripPreservesConnectionsAndPrerequisitesOfUnplacedFactory()
        {
            var sample = Sample();
            var forest = new WorldRoomRef("forest", "forest.room.json", new Cell(18, 0), new[] { "factory" });
            var draft = new WorldDocument(sample.WorldId, sample.Min, sample.Max, new[] { sample.Rooms[0], forest },
                sample.Connections, sample.Start, new WorldEndpoint("forest", "exit"), new[] { "factory" });
            var catalog = new[] { "warehouse", "factory", "forest" };
            string text = WorldCodec.EncodeDraft(draft, catalog);
            var restored = WorldCodec.DecodeDraft(text, catalog);
            Assert.Equal(new[] { "factory" }, restored.Rooms[1].ForwardRequiresSolved);
            Assert.Equal(new[] { "factory" }, restored.FinishRequiresSolved);
            Assert.Equal("factory", Assert.Single(restored.Connections).To.LevelId);
            Assert.Throws<FormatException>(() => WorldCodec.Decode(text));
            Assert.Throws<InvalidOperationException>(() => WorldCodec.Encode(draft));
        }

        [Fact]
        public void DraftRejectsReferencesOutsideTheCatalog()
        {
            var catalog = new[] { "warehouse", "factory" };
            string text = WorldCodec.EncodeDraft(Sample(), catalog)
                .Replace("\"levelId\":\"factory\",\"doorId\":\"entry\"", "\"levelId\":\"ghost\",\"doorId\":\"entry\"");
            Assert.Throws<FormatException>(() => WorldCodec.DecodeDraft(text, catalog));
        }

        static WorldDocument Sample() => new WorldDocument(
            "main", new Cell(0, 0), new Cell(63, 31),
            new List<WorldRoomRef>
            {
                new WorldRoomRef("warehouse", "warehouse.room.json", new Cell(0, 0), new string[0]),
                new WorldRoomRef("factory", "factory.room.json", new Cell(9, 0), new[] { "warehouse" }),
            },
            new List<WorldConnection>
            {
                new WorldConnection("link-1", "direct", new WorldEndpoint("warehouse", "exit"), new WorldEndpoint("factory", "entry"), true),
            },
            new WorldEndpoint("warehouse", "spawn"),
            new WorldEndpoint("factory", "exit"),
            new[] { "warehouse", "factory" });

        [Fact]
        public void RoundTripKeepsBoundsRoomsAndConnections()
        {
            var back = WorldCodec.Decode(WorldCodec.Encode(Sample()));
            Assert.Equal("main", back.WorldId);
            Assert.Equal(new Cell(63, 31), back.Max);
            Assert.Equal(2, back.Rooms.Count);
            Assert.Equal(new Cell(9, 0), back.Rooms[1].Origin);
            Assert.Equal(new[] { "warehouse" }, back.Rooms[1].ForwardRequiresSolved);
            var link = back.Connections.Single();
            Assert.Equal("direct", link.Mode);
            Assert.Equal("exit", link.From.DoorId);
            Assert.Equal("factory", link.To.LevelId);
            Assert.True(link.AllowReturn);
        }

        [Fact]
        public void RoundTripKeepsStartAndFinish()
        {
            var back = WorldCodec.Decode(WorldCodec.Encode(Sample()));
            Assert.Equal("spawn", back.Start.DoorId);
            Assert.Equal("factory", back.FinishEndpoint.LevelId);
            Assert.Equal(new[] { "warehouse", "factory" }, back.FinishRequiresSolved);
        }

        [Fact]
        public void GarbageTextIsRejected()
        {
            Assert.Throws<FormatException>(() => WorldCodec.Decode("nope"));
            Assert.Throws<FormatException>(() => WorldCodec.Decode("{}"));
        }

        [Fact]
        public void DuplicateLevelIdIsRejected()
        {
            string text = WorldCodec.Encode(Sample()).Replace("\"factory\",\"file\"", "\"warehouse\",\"file\"");
            Assert.Throws<FormatException>(() => WorldCodec.Decode(text));
        }

        [Fact]
        public void ConnectionToUnknownRoomIsRejected()
        {
            string text = WorldCodec.Encode(Sample()).Replace("\"levelId\":\"factory\",\"doorId\":\"entry\"", "\"levelId\":\"ghost\",\"doorId\":\"entry\"");
            Assert.Throws<FormatException>(() => WorldCodec.Decode(text));
        }

        [Fact]
        public void UnknownModeIsRejected()
        {
            string text = WorldCodec.Encode(Sample()).Replace("\"direct\"", "\"warp\"");
            Assert.Throws<FormatException>(() => WorldCodec.Decode(text));
        }

        [Fact]
        public void WrongKindOrFormatVersionIsRejected()
        {
            string text = WorldCodec.Encode(Sample());
            Assert.Throws<FormatException>(() => WorldCodec.Decode(text.Replace("\"kind\":\"world\"", "\"kind\":\"room\"")));
            Assert.Throws<FormatException>(() => WorldCodec.Decode(text.Replace("\"formatVersion\":1", "\"formatVersion\":9")));
        }

        [Fact]
        public void InvertedBoundsAreRejected()
        {
            string text = WorldCodec.Encode(Sample()).Replace("[63,31]", "[-5,-5]");
            Assert.Throws<FormatException>(() => WorldCodec.Decode(text));
        }
    }
}
