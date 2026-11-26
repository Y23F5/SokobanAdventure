using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class LegacyPassthroughTests
    {
        const string Raw = "line1\nline2 \"q\" \\ 仓\t";

        static LegacyRoomData Sample() => new LegacyRoomData
        {
            LevelId = "warehouse",
            Width = 9,
            Height = 7,
            RowsNorthToSouth = new[] { "#########", "#.......#", "#.......#", ".........", "#.......#", "#.......#", "#########" },
            Doors = new[]
            {
                new LegacyDoor("entry", new Cell(0, 3), "W", "entry"),
                new LegacyDoor("exit", new Cell(8, 3), "E", "exit"),
            },
            PrimaryEntranceId = "entry",
            Boxes = new[] { new LegacyEntity("box-1", new Cell(4, 2)) },
            Targets = new[] { new LegacyEntity("target-1", new Cell(6, 2)) },
            Spawn = new LegacyEntity("spawn", new Cell(2, 2)),
            Extras = new Dictionary<string, string>
            {
                ["name"] = "\"仓库\"",
                ["themeId"] = "\"warehouse\"",
                ["events"] = "[{\"id\":\"e1\",\"on\":\"enter\"}]",
                ["decoration:z-deco"] = "{\"id\":\"z-deco\",\"cell\":[5,4]}",
                ["stateProfile:box-1"] = Raw,
            },
        };

        static RoomDocument Document(RoomDraftModel d) =>
            new RoomDocument(d.Room, d.Doors, d.PrimaryEntranceId, d.RecoveryItems, d.Extras);

        [Fact]
        public void AdapterCopiesExtrasIntoDraftVerbatim()
        {
            var draft = LegacyRoomAdapter.ToDraft(Sample());
            Assert.Equal(5, draft.Extras.Count);
            Assert.Equal("[{\"id\":\"e1\",\"on\":\"enter\"}]", draft.Extras["events"]);
            Assert.Equal(Raw, draft.Extras["stateProfile:box-1"]);
        }

        [Fact]
        public void DecorationTextStaysInExtrasAndTakesNoCell()
        {
            var draft = LegacyRoomAdapter.ToDraft(Sample());
            Assert.Null(draft.Room.ObjectAt(new Cell(5, 4)));
            Assert.Empty(draft.Room.ObjectsOf(ObjectCategories.Custom));
            Assert.Equal("{\"id\":\"z-deco\",\"cell\":[5,4]}", draft.Extras["decoration:z-deco"]);
        }

        [Fact]
        public void DraftExtrasAreIndependentOfSourceDictionary()
        {
            var data = Sample();
            var draft = LegacyRoomAdapter.ToDraft(data);
            data.Extras["name"] = "\"changed\"";
            Assert.Equal("\"仓库\"", draft.Extras["name"]);
        }

        [Fact]
        public void DraftWithoutExtrasHasEmptyBag()
        {
            var draft = new RoomDraftModel(new Room("r", 3, 3));
            Assert.NotNull(draft.Extras);
            Assert.Empty(draft.Extras);
        }

        [Fact]
        public void NullExtrasInLegacyDataGivesEmptyBag()
        {
            var data = Sample();
            data.Extras = null;
            Assert.Empty(LegacyRoomAdapter.ToDraft(data).Extras);
        }

        [Fact]
        public void LegacyDataDefaultsToNoExtras()
        {
            Assert.Empty(new LegacyRoomData().Extras);
        }

        [Fact]
        public void WriterReturnsExtrasUnchangedAndInOrder()
        {
            var back = LegacyRoomWriter.FromDraft(LegacyRoomAdapter.ToDraft(Sample()));
            Assert.Equal(Sample().Extras.ToArray(), back.Extras.ToArray());
        }

        [Fact]
        public void WriterReflectsEditedExtras()
        {
            var draft = LegacyRoomAdapter.ToDraft(Sample());
            draft.Extras["name"] = "\"new\"";
            draft.Extras.Remove("events");
            var back = LegacyRoomWriter.FromDraft(draft);
            Assert.Equal("\"new\"", back.Extras["name"]);
            Assert.False(back.Extras.ContainsKey("events"));
        }

        [Fact]
        public void CodecRoundTripKeepsExtrasVerbatim()
        {
            var back = RoomCodec.Decode(RoomCodec.Encode(Document(LegacyRoomAdapter.ToDraft(Sample()))));
            Assert.Equal(Sample().Extras.ToArray(), back.Extras.ToArray());
        }

        [Fact]
        public void CodecOmitsExtrasWhenAbsent()
        {
            var data = Sample();
            data.Extras = null;
            Assert.DoesNotContain("extras", RoomCodec.Encode(Document(LegacyRoomAdapter.ToDraft(data))));
        }

        [Fact]
        public void CodecDecodesTextWithoutExtras()
        {
            var doc = new RoomDocument(LegacyRoomAdapter.ToDraft(Sample()).Room, new List<DoorInfo>(), null, null);
            Assert.Empty(RoomCodec.Decode(RoomCodec.Encode(doc)).Extras);
        }

        [Fact]
        public void CodecRejectsNonStringExtra()
        {
            var doc = new RoomDocument(new Room("r", 2, 2), null, null, null, new Dictionary<string, string> { ["k"] = "v" });
            string text = RoomCodec.Encode(doc).Replace("\"k\":\"v\"", "\"k\":5");
            Assert.Throws<FormatException>(() => RoomCodec.Decode(text));
        }
    }
}
