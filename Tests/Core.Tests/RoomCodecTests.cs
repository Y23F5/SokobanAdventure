using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RoomCodecTests
    {
        static RoomDocument Sample()
        {
            var r = Blueprint.Parse("warehouse", new[] { "######", "<.PBT>", "######" });
            var items = new List<RecoveryItem>
            {
                new RecoveryItem("recovery-1", "warehouse-lift", 3.7f,
                    new Dictionary<string, float> { { "gateX", 4.25f }, { "yaw", 90f } })
            };
            return new RoomDocument(r.Room, r.Doors, "entry", items);
        }

        [Fact]
        public void RoundTripKeepsTerrainObjectsAndDoors()
        {
            var back = RoomCodec.Decode(RoomCodec.Encode(Sample()));
            Assert.Equal("warehouse", back.Room.Id);
            Assert.Equal(6, back.Room.Width);
            Assert.Equal(3, back.Room.Height);
            Assert.Equal(TerrainCategories.Door, back.Room.TerrainAt(new Cell(0, 1)).CategoryId);
            Assert.Equal(TerrainCategories.Wall, back.Room.TerrainAt(new Cell(0, 0)).CategoryId);
            Assert.Equal(ObjectCategories.Spawn, back.Room.ObjectAt(new Cell(2, 1)).CategoryId);
            Assert.Equal(ObjectCategories.Box, back.Room.ObjectAt(new Cell(3, 1)).CategoryId);
            Assert.Equal(2, back.Doors.Count);
            Assert.Equal("entry", back.PrimaryEntranceId);
            var exit = back.Doors.Single(d => d.Role == "exit");
            Assert.Equal(new Cell(5, 1), exit.Cell);
            Assert.Equal(Cell.Right, exit.Outward);
        }

        [Fact]
        public void RoundTripKeepsRecoveryItemWithFractionalValues()
        {
            var item = RoomCodec.Decode(RoomCodec.Encode(Sample())).RecoveryItems.Single();
            Assert.Equal("recovery-1", item.Id);
            Assert.Equal("warehouse-lift", item.ScriptRef);
            Assert.Equal(3.7f, item.Duration, 3);
            Assert.Equal(4.25f, item.Params["gateX"], 3);
            Assert.Equal(90f, item.Params["yaw"], 3);
        }

        [Fact]
        public void EncodedTextHasNoDecimalPoint()
        {
            Assert.DoesNotContain(".", RoomCodec.Encode(Sample()).Replace("warehouse-lift", ""));
        }

        [Fact]
        public void OpenableTerrainKeepsEntityAndState()
        {
            var r = Blueprint.Parse("forest", new[] { "#####", "#PVB#", "#####" });
            var doc = new RoomDocument(r.Room, r.Doors, null, new List<RecoveryItem>());
            var back = RoomCodec.Decode(RoomCodec.Encode(doc));
            var cell = back.Room.TerrainAt(new Cell(2, 1));
            Assert.Equal(TerrainCategories.Openable, cell.CategoryId);
            Assert.Equal("vine-1", cell.EntityId);
            Assert.Equal(TerrainCategories.Closed, cell.State);
            Assert.True(back.Room.Ids.Contains("vine-1"));
            Assert.Null(back.PrimaryEntranceId);
        }

        [Fact]
        public void RecoveryItemIdSharesTheRoomIdSpace()
        {
            var back = RoomCodec.Decode(RoomCodec.Encode(Sample()));
            Assert.True(back.Room.Ids.Contains("recovery-1"));
        }

        [Fact]
        public void GarbageTextIsRejected()
        {
            Assert.Throws<FormatException>(() => RoomCodec.Decode("not json"));
            Assert.Throws<FormatException>(() => RoomCodec.Decode("[]"));
            Assert.Throws<FormatException>(() => RoomCodec.Decode("{}"));
        }

        [Fact]
        public void DuplicateIdIsRejected()
        {
            string text = RoomCodec.Encode(Sample()).Replace("\"recovery-1\"", "\"box-1\"");
            Assert.Throws<FormatException>(() => RoomCodec.Decode(text));
        }

        [Fact]
        public void UnknownTerrainCategoryIsRejected()
        {
            string text = RoomCodec.Encode(Sample()).Replace("\"wall\"", "\"lava\"");
            Assert.Throws<FormatException>(() => RoomCodec.Decode(text));
        }

        [Fact]
        public void WrongFormatVersionIsRejected()
        {
            string text = RoomCodec.Encode(Sample()).Replace("\"formatVersion\":1", "\"formatVersion\":99");
            Assert.Throws<FormatException>(() => RoomCodec.Decode(text));
        }

        [Fact]
        public void NonPositiveDurationIsRejectedOnRead()
        {
            string text = RoomCodec.Encode(Sample()).Replace("\"duration\":3700", "\"duration\":0");
            Assert.Throws<FormatException>(() => RoomCodec.Decode(text));
        }
    }
}
