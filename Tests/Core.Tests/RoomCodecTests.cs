using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class RoomCodecTests
    {
        static RoomDraftModel Sample()
        {
            var r = Blueprint.Parse("warehouse", new[] { "######", "<.PBT>", "######" });
            var items = new List<RecoveryItem>
            {
                new RecoveryItem("recovery-1", "warehouse-lift", 3.7f,
                    new Dictionary<string, float> { { "gateX", 4.25f }, { "yaw", 90f } })
            };
            return new RoomDraftModel(r.Room, r.Doors, "entry", null, items);
        }

        static RoomDraftModel Rich()
        {
            var draft = Sample();
            var room = draft.Room;
            draft.Meta = new RoomMeta("仓库", "warehouse-theme");
            room.SetAppearance(new Cell(1, 1), new AppearanceRef("floor-wood", 3));
            room.SetObjectAppearance("box-1", new AppearanceRef("box-crate", 2));
            room.Ids.Add("deco-1", "decoration");
            draft.Decorations.Add(new Decoration("deco-1", new Cell(-2, 7), new AppearanceRef("tree", 1)));
            draft.Assets.Add(new AssetDescriptor("model-abc", AssetKinds.Model, "桶", "abc", ".glb"));
            draft.Assets.Add(new AssetDescriptor("ui-1", AssetKinds.UiImage, "图", "def", ".png"));
            draft.UiImageAssetId = "ui-1";
            var nodes = new[]
            {
                new BehaviorNode("n1", BehaviorNodeKinds.RoomSolved, new Cell(2, 1)),
                new BehaviorNode("n2", BehaviorNodeKinds.SetVisible, null, new Dictionary<string, string> { { "target", "box-1" } }),
            };
            var edges = new[] { new BehaviorEdge("n1", "out", "n2", "in") };
            room.Ids.Add("graph-1", "graph");
            draft.Graphs.Add(new BehaviorGraph("graph-1", "box-1", nodes, edges));
            var table = new ObjectStateTable("intact", new[] { "intact", "decayed" },
                new[] { new StateTransition("intact", TriggerIds.FirstPush, "decayed", new AppearanceRef("remnant", 0)) });
            draft.ObjectStates["box-1"] = table;
            draft.Templates["tpl-1"] = new ObjectTemplate("tpl-1", "模板", new[] { new BehaviorGraph("tg", null, new[] { new BehaviorNode("t1", BehaviorNodeKinds.Lua, new Cell(0, 0)) }, null) }, table, new AppearanceRef("tpl-look", 1));
            room.Ids.Add("inst-1", "instance");
            draft.Instances.Add(new TemplateInstance("inst-1", "tpl-1", new Dictionary<string, string> { { "x", "1" } }));
            draft.Extras["note"] = "hello";
            return draft;
        }

        [Fact]
        public void HintObjectKeepsTextAndRotationOnReopen()
        {
            var draft = Sample();
            draft.Room.AddObject("hint-1", ObjectCategories.Hint, new Cell(1, 1), rotation: 2);
            draft.Room.SetHintText("hint-1", "先看右边。\n再推箱子。");
            var reopened = RoomCodec.Decode(RoomCodec.Encode(draft)).Room.ObjectsOf(ObjectCategories.Hint).Single();
            Assert.Equal(new Cell(1, 1), reopened.Cell);
            Assert.Equal(2, reopened.Rotation);
            Assert.Equal("先看右边。\n再推箱子。", reopened.HintText);
        }

        [Fact]
        public void RoomTextSurvivesRoundTripAndThemeChange()
        {
            var draft = Sample();
            draft.Meta = new RoomMeta("仓库", "warehouse", "这里堆放备件。", "从入口向前。", "先推右侧木箱。")
                .WithIdentity("货仓", "factory");
            var reopened = RoomCodec.Decode(RoomCodec.Encode(draft)).Meta;
            Assert.Equal("货仓", reopened.Name);
            Assert.Equal("factory", reopened.ThemeId);
            Assert.Equal("这里堆放备件。", reopened.Description);
            Assert.Equal("从入口向前。", reopened.EntryHint);
            Assert.Equal("先推右侧木箱。", reopened.PlayHint);
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
            var draft = new RoomDraftModel(r.Room, r.Doors, null, null, new List<RecoveryItem>());
            var back = RoomCodec.Decode(RoomCodec.Encode(draft));
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
        public void EveryFieldSurvivesTheRoundTrip()
        {
            var back = RoomCodec.Decode(RoomCodec.Encode(Rich()));
            Assert.Equal("仓库", back.Meta.Name);
            Assert.Equal("warehouse-theme", back.Meta.ThemeId);
            Assert.Equal(new AppearanceRef("floor-wood", 3), back.Room.AppearanceAt(new Cell(1, 1)));
            Assert.Equal(new AppearanceRef("box-crate", 2), back.Room.Objects.Single(o => o.Id == "box-1").Appearance);
            var deco = back.Decorations.Single();
            Assert.Equal(new Cell(-2, 7), deco.Cell);
            Assert.Equal(new AppearanceRef("tree", 1), deco.Appearance);
            Assert.Equal(2, back.Assets.Count);
            Assert.Equal("abc", back.Assets[0].Sha256);
            Assert.Equal("ui-1", back.UiImageAssetId);
            var graph = back.Graphs.Single();
            Assert.Equal("box-1", graph.OwnerId);
            Assert.Equal(new Cell(2, 1), graph.Nodes[0].Cell);
            Assert.Null(graph.Nodes[1].Cell);
            Assert.Equal("box-1", graph.Nodes[1].Parameters["target"]);
            Assert.Equal("in", graph.Edges.Single().ToPort);
            var transition = back.ObjectStates["box-1"].Find("intact", TriggerIds.FirstPush);
            Assert.Equal("decayed", transition.To);
            Assert.Equal(new AppearanceRef("remnant", 0), transition.Remnant);
            var tpl = back.Templates["tpl-1"];
            Assert.Equal("模板", tpl.Name);
            Assert.Equal(new Cell(0, 0), tpl.Graphs[0].Nodes[0].Cell);
            Assert.Equal(new AppearanceRef("tpl-look", 1), tpl.Appearance);
            Assert.Equal("1", back.Instances.Single().Overrides["x"]);
            Assert.Equal("hello", back.Extras["note"]);
            Assert.True(back.Room.Ids.Contains("inst-1"));
            Assert.True(back.Room.Ids.Contains("graph-1"));
        }

        [Fact]
        public void SecondEncodeIsIdenticalToTheFirst()
        {
            string first = RoomCodec.Encode(Rich());
            Assert.Equal(first, RoomCodec.Encode(RoomCodec.Decode(first)));
        }

        [Fact]
        public void UnknownResourceKindIsRejected()
        {
            string text = RoomCodec.Encode(Rich()).Replace("\"kind\":\"model\"", "\"kind\":\"hologram\"");
            Assert.Throws<FormatException>(() => RoomCodec.Decode(text));
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

        [Fact]
        public void EncodeRefusesADraftThatCannotBeReadBack()
        {
            var draft = Sample();
            draft.Room.SetTerrain(new Cell(1, 1), "lava");
            Assert.Throws<FormatException>(() => RoomCodec.Encode(draft));
        }
    }
}
