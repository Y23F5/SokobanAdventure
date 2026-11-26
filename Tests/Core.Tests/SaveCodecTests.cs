using System;
using System.Collections.Generic;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class SaveCodecTests
    {
        static readonly string[] Rows = { "#####", "#P.T#", "#.B.#", "#####" };

        static Room MakeRoom() => Blueprint.Parse("r1", Rows).Room;

        static SaveData MakeData(Room room)
        {
            var data = new SaveData { WorldId = "w", RoomId = "r1", PlayerX = 1, PlayerY = 2 };
            data.Boxes["r1"] = new Dictionary<string, int[]> { ["box-1"] = new[] { 2, 1 } };
            data.Hashes["r1"] = ContentHash.Of(new RoomDraftModel(room));
            return data;
        }

        static Dictionary<string, string> Hashes(Room room) => new Dictionary<string, string> { ["r1"] = ContentHash.Of(new RoomDraftModel(room)) };

        [Theory]
        [InlineData(null, SaveStatus.Ok)]
        [InlineData(3, SaveStatus.Ok)]
        [InlineData(4, SaveStatus.Corrupt)]
        public void FacingIsAnOptionalFourWayField(int? facing, SaveStatus status)
        {
            var room = MakeRoom();
            var data = MakeData(room);
            data.Facing = facing;
            var load = SaveCodec.Decode(SaveCodec.ToJson(data), "w", Hashes(room));
            Assert.Equal(status, load.Status);
            if (status == SaveStatus.Ok) Assert.Equal(facing, load.Data.Facing);
        }

        [Fact]
        public void RoundTripIsOk()
        {
            var room = MakeRoom();
            var text = SaveCodec.Encode(MakeData(room));
            var load = SaveCodec.Decode(text, "w", Hashes(room));
            Assert.Equal(SaveStatus.Ok, load.Status);
            Assert.Equal(2, load.Data.Boxes["r1"]["box-1"][0]);
        }

        [Fact]
        public void EmptyTextIsNoSave()
        {
            Assert.Equal(SaveStatus.NoSave, SaveCodec.Decode("", "w", new Dictionary<string, string>()).Status);
            Assert.Equal(SaveStatus.NoSave, SaveCodec.Decode(null, "w", new Dictionary<string, string>()).Status);
        }

        [Fact]
        public void BrokenJsonAndMissingFieldsAreCorrupt()
        {
            var h = new Dictionary<string, string>();
            Assert.Equal(SaveStatus.Corrupt, SaveCodec.Decode("{not json", "w", h).Status);
            Assert.Equal(SaveStatus.Corrupt, SaveCodec.Decode("{}", "w", h).Status);
        }

        [Fact]
        public void BadBoxCellIsCorrupt()
        {
            var room = MakeRoom();
            var data = MakeData(room);
            data.Boxes["r1"]["box-1"] = new[] { 1 };
            var text = SaveCodec.ToJson(data);
            Assert.Equal(SaveStatus.Corrupt, SaveCodec.Decode(text, "w", Hashes(room)).Status);
        }

        [Fact]
        public void WorldMismatchIsIncompatible()
        {
            var room = MakeRoom();
            var text = SaveCodec.Encode(MakeData(room));
            Assert.Equal(SaveStatus.Incompatible, SaveCodec.Decode(text, "other", Hashes(room)).Status);
        }

        [Fact]
        public void ChangedGameplayContentIsIncompatible()
        {
            var room = MakeRoom();
            var text = SaveCodec.Encode(MakeData(room));
            var changed = Blueprint.Parse("r1", new[] { "#####", "#P.T#", "#B..#", "#####" }).Room;
            var load = SaveCodec.Decode(text, "w", Hashes(changed));
            Assert.Equal(SaveStatus.Incompatible, load.Status);
        }

        [Fact]
        public void WrongFormatVersionIsIncompatible()
        {
            var room = MakeRoom();
            var data = MakeData(room);
            data.FormatVersion = 99;
            var text = SaveCodec.ToJson(data);
            Assert.Equal(SaveStatus.Incompatible, SaveCodec.Decode(text, "w", Hashes(room)).Status);
        }

        [Fact]
        public void HashIgnoresNothingButGameplayAndIsStable()
        {
            var a = ContentHash.Of(new RoomDraftModel(MakeRoom()));
            var b = ContentHash.Of(new RoomDraftModel(MakeRoom()));
            Assert.Equal(a, b);
            var moved = Blueprint.Parse("r1", new[] { "#####", "#P.T#", "#..B#", "#####" }).Room;
            Assert.NotEqual(a, ContentHash.Of(new RoomDraftModel(moved)));
        }

        [Fact]
        public void EncodeRefusesDataThatFailsProbe()
        {
            var room = MakeRoom();
            var data = MakeData(room);
            data.RoomId = "missing";
            Assert.Throws<InvalidOperationException>(() => SaveCodec.Encode(data));
        }
    }
}
