using System;
using System.Linq;
using System.Reflection;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class DesignAlignmentTests
    {
        [Fact]
        public void PlacedObjectStoresIdentityPlacementAndAppearance()
        {
            var names = typeof(PlacedObject).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).OrderBy(n => n).ToArray();
            Assert.Equal(new[] { "Appearance", "CategoryId", "Cell", "Id", "Rotation" }, names);
        }

        [Fact]
        public void ValidatorHasNoRecoveryCellRules()
        {
            var room = new Room("r", 5, 5);
            var draft = new RoomDraftModel(room);
            Assert.DoesNotContain(RoomValidator.ValidateDraft(draft, ValidationStage.Publish), i => i.RuleId.StartsWith("box.recovery", StringComparison.Ordinal));
        }

        [Fact]
        public void RoomCodecWritesNoRecoveryOrDecorationFields()
        {
            var room = new Room("r", 3, 3);
            room.SetTerrain(new Cell(1, 1), TerrainCategories.Floor);
            room.AddObject("box-1", ObjectCategories.Box, new Cell(1, 1));
            string text = RoomCodec.Encode(new RoomDraftModel(room));
            Assert.DoesNotContain("recovery\"", text.Replace("\"recovery\":[]", ""));
            Assert.DoesNotContain("decorationKind", text);
            Assert.DoesNotContain("appearanceRef", text);
        }

        [Fact]
        public void WorldStartIsReadFromSpawnIdOnly()
        {
            var doc = new WorldDocument("w", new Cell(0, 0), new Cell(19, 9),
                new[] { new WorldRoomRef("a", "a.json", new Cell(0, 0), new string[0]) },
                new WorldConnection[0], new WorldEndpoint("a", "spawn"), new WorldEndpoint("a", "exit"), new[] { "a" });
            string text = WorldCodec.Encode(doc).Replace("\"spawnId\"", "\"doorId\"");
            Assert.Throws<FormatException>(() => WorldCodec.Decode(text));
        }

        [Fact]
        public void WorldAllowReturnMustBeBoolean()
        {
            var doc = new WorldDocument("w", new Cell(0, 0), new Cell(19, 9),
                new[] { new WorldRoomRef("a", "a.json", new Cell(0, 0), new string[0]), new WorldRoomRef("b", "b.json", new Cell(6, 0), new string[0]) },
                new[] { new WorldConnection("l", "direct", new WorldEndpoint("a", "exit"), new WorldEndpoint("b", "entry"), true) },
                new WorldEndpoint("a", "spawn"), new WorldEndpoint("b", "exit"), new[] { "a" });
            string text = WorldCodec.Encode(doc).Replace("\"allowReturn\":true", "\"allowReturn\":1");
            Assert.Throws<FormatException>(() => WorldCodec.Decode(text));
        }
    }
}
