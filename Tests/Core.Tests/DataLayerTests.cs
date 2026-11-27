/**
 * @file DataLayerTests.cs
 * @brief 数据层整体替换后的新增行为测试：内容哈希范围、存档扩展字段与三态、扩建平移覆盖全部带格坐标的数据、ValidatedRoom 生成与新增校验规则。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using Xunit;

namespace Sokoban.Core.Tests
{
    public class DataLayerTests
    {
        static RoomDraftModel Good()
        {
            var draft = BrushFixture.Draft();
            draft.Room.SetTerrain(new Cell(5, 5), TerrainCategories.Wall);
            new SetSpawnCommand(draft).Apply();
            return draft;
        }

        [Fact]
        public void HashIgnoresAppearanceNamesAndDecorations()
        {
            var a = Good();
            var before = ContentHash.Of(a);
            a.Room.SetAppearance(new Cell(2, 2), new AppearanceRef("floor-x", 4));
            a.Room.SetObjectAppearance("box-1", new AppearanceRef("crate", 1));
            a.Meta = new RoomMeta("改名", "theme");
            a.Room.Ids.Add("deco", "decoration");
            a.Decorations.Add(new Decoration("deco", new Cell(-1, -1), new AppearanceRef("tree", 0)));
            Assert.Equal(before, ContentHash.Of(a));
        }

        [Fact]
        public void HashCoversRecoveryDeviceAndGraphs()
        {
            var a = Good();
            var device = new RecoveryDevice("device", RecoveryDeviceKind.Forklift, new Cell(-1, 2), 1, "warehouse-lift", 3.7f);
            a.ReplaceRecoveryDevice(device);
            var before = ContentHash.Of(a);
            RecoveryDeviceCommands.SetDetails(new DraftHolder(a), 5f, null, null, null).Apply();
            var afterDuration = ContentHash.Of(a);
            Assert.NotEqual(before, afterDuration);
            a.Graphs.Add(new BehaviorGraph("g", null, new[] { new BehaviorNode("n", BehaviorNodeKinds.RoomSolved, new Cell(1, 1)) }, null));
            Assert.NotEqual(afterDuration, ContentHash.Of(a));
        }

        [Fact]
        public void SaveExtendedFieldsRoundTrip()
        {
            var draft = Good();
            var hash = ContentHash.Of(draft);
            var data = new SaveData { WorldId = "w", RoomId = "r", PlayerX = 1, PlayerY = 3 };
            data.Hashes["r"] = hash;
            data.Explored.Add("r");
            data.Items.AddRange(new[] { "recovery-1", "recovery-2" });
            data.SelectedItem = "recovery-2";
            data.PendingItem = "recovery-1";
            data.EverSolved["r"] = true;
            data.ObjectStates["r"] = new Dictionary<string, string> { ["box-1"] = "decayed" };
            data.Presentation = new PresentationSegment
            {
                RecoveryT = 0.25f, RecoveryDuration = 3.7f, RecoveryDeviceId = "device", Heading = 90f, MotionKind = "push",
                MotionFraction = 0.5f, MotionDelta = Cell.Right, MotionPushed = true
            };
            var load = SaveCodec.Decode(SaveCodec.Encode(data), "w", data.Hashes);
            Assert.Equal(SaveStatus.Ok, load.Status);
            var back = load.Data;
            Assert.Contains("r", back.Explored);
            Assert.Equal(new[] { "recovery-1", "recovery-2" }, back.Items);
            Assert.Equal("recovery-2", back.SelectedItem);
            Assert.Equal("recovery-1", back.PendingItem);
            Assert.True(back.EverSolved["r"]);
            Assert.Equal("decayed", back.ObjectStates["r"]["box-1"]);
            Assert.Equal(0.25f, back.Presentation.RecoveryT, 3);
            Assert.Equal(3.7f, back.Presentation.RecoveryDuration, 3);
            Assert.Equal("device", back.Presentation.RecoveryDeviceId);
            Assert.Equal(90f, back.Presentation.Heading, 3);
            Assert.Equal("push", back.Presentation.MotionKind);
            Assert.Equal(0.5f, back.Presentation.MotionFraction, 3);
            Assert.Equal(Cell.Right, back.Presentation.MotionDelta);
            Assert.True(back.Presentation.MotionPushed);
        }

        [Fact]
        public void SaveWithSelectedItemOutsideBagIsCorrupt()
        {
            var data = new SaveData { WorldId = "w", RoomId = "r" };
            data.Hashes["r"] = "h";
            data.Items.Add("a");
            data.SelectedItem = "zzz";
            var load = SaveCodec.Decode(SaveCodec.ToJson(data), "w", data.Hashes);
            Assert.Equal(SaveStatus.Corrupt, load.Status);
            Assert.Throws<InvalidOperationException>(() => SaveCodec.Encode(data));
        }

        [Fact]
        public void SaveWithInvalidRecoveryProgressIsCorrupt()
        {
            var data = new SaveData { WorldId = "w", RoomId = "r" };
            data.Hashes["r"] = "h";
            data.Presentation.RecoveryT = .5f;
            data.Presentation.RecoveryDuration = 3.7f;
            string invalid = SaveCodec.Encode(data).Replace("\"recoveryT\":500", "\"recoveryT\":1001");
            Assert.Equal(SaveStatus.Corrupt, SaveCodec.Decode(invalid, "w", data.Hashes).Status);
        }

        [Fact]
        public void ShortRecoveryAndUnfinishedMotionResumeFromRoundedProgress()
        {
            var data = new SaveData { WorldId = "w", RoomId = "r" };
            data.Hashes["r"] = "h";
            data.Presentation.RecoveryDuration = .4f;
            data.Presentation.RecoveryT = .9999f;
            data.Presentation.MotionKind = "turn";
            data.Presentation.MotionDelta = Cell.Right;
            data.Presentation.MotionFraction = .9999f;
            string text = SaveCodec.Encode(data);
            Assert.Contains("\"recoveryT\":999", text);
            Assert.Contains("\"motionFraction\":999", text);
            string old = text.Replace("\"recoveryT\":999", "\"recoveryT\":1000")
                .Replace("\"motionFraction\":999", "\"motionFraction\":1000");
            var load = SaveCodec.Decode(old, "w", data.Hashes);
            Assert.Equal(SaveStatus.Ok, load.Status);
            Assert.Equal(.999f, load.Data.Presentation.RecoveryT);
            Assert.Equal(.999f, load.Data.Presentation.MotionFraction);
        }

        [Fact]
        public void ExpandShiftsEverythingAndRevertRestores()
        {
            var draft = Good();
            draft.Room.SetAppearance(new Cell(2, 2), new AppearanceRef("floor-x", 4));
            draft.Room.Ids.Add("deco", "decoration");
            draft.Decorations.Add(new Decoration("deco", new Cell(-1, 2), new AppearanceRef("tree", 0)));
            draft.Room.Ids.Add("g1", "graph");
            draft.Graphs.Add(new BehaviorGraph("g1", "box-1", new[] { new BehaviorNode("n1", BehaviorNodeKinds.RoomSolved, new Cell(3, 3)) }, null));
            var holder = new DraftHolder(draft);
            var cmd = new ExpandDraftCommand(holder, 2, 1, 0, 0);
            cmd.Apply();
            var next = holder.Draft;
            Assert.Equal(new Cell(5, 4), next.Room.Objects.Single(o => o.Id == "box-1").Cell);
            Assert.Equal(new AppearanceRef("floor-x", 4), next.Room.AppearanceAt(new Cell(4, 3)));
            Assert.Equal(new Cell(1, 3), next.Decorations.Single().Cell);
            Assert.Equal(new Cell(5, 4), next.Graphs.Single().Nodes[0].Cell);
            Assert.Equal(new Cell(-1, 3), next.RecoveryDevice.Anchor);
            cmd.Revert();
            var back = holder.Draft;
            Assert.Equal(7, back.Room.Width);
            Assert.Equal(new Cell(3, 3), back.Room.Objects.Single(o => o.Id == "box-1").Cell);
            Assert.Equal(new Cell(-1, 2), back.Decorations.Single().Cell);
            Assert.Equal(new Cell(3, 3), back.Graphs.Single().Nodes[0].Cell);
            Assert.Equal(draft.RecoveryDevice.Anchor, back.RecoveryDevice.Anchor);
            Assert.Equal(ContentHash.Of(draft), ContentHash.Of(back));
        }

        [Fact]
        public void ExpandOverLimitDoesNotCommit()
        {
            var history = new CommandHistory();
            var holder = new DraftHolder(Good());
            Assert.Throws<InvalidOperationException>(() => history.Execute(new ExpandDraftCommand(holder, 10, 0, 0, 0, 8)));
            Assert.False(history.CanUndo);
        }

        [Fact]
        public void ValidatedRoomIsBuiltFromAGoodDraft()
        {
            var draft = Good();
            draft.ReplaceRecoveryDevice(new RecoveryDevice("device", RecoveryDeviceKind.Forklift,
                new Cell(-1, 2), 1, "warehouse-lift", RecoveryDevice.DefaultDuration));
            var v = ValidatedRoom.From(draft, new RoomMeta("名", "t"));
            Assert.Equal("名", v.Meta.Name);
            Assert.Equal(ObjectCategories.Spawn, v.Spawn.CategoryId);
            Assert.Equal("entry", v.PrimaryEntrance.Id);
            Assert.Equal(new Cell(1, 3), v.EntryBuffer);
            Assert.Same(draft.RecoveryDevice, v.RecoveryDevice);
        }

        [Fact]
        public void ValidatedRoomFailureNamesRuleAndLocation()
        {
            var draft = BrushFixture.Draft();
            var e = Assert.Throws<InvalidOperationException>(() => ValidatedRoom.From(draft, null));
            Assert.Contains("spawn.missing", e.Message);
        }

        [Fact]
        public void DuplicateIdAndDanglingReferenceAreErrors()
        {
            var draft = Good();
            draft.Graphs.Add(new BehaviorGraph("box-1", "nobody", null, null));
            draft.Instances.Add(new TemplateInstance("i1", "ghost"));
            draft.ObjectStates["phantom"] = new ObjectStateTable("a", new[] { "a" }, null);
            var issues = RoomValidator.ValidateDraft(draft, ValidationStage.Draft);
            Assert.Contains(issues, i => i.RuleId == "id.duplicate" && i.Location == "box-1" && i.Severity == Severity.Error);
            Assert.Contains(issues, i => i.RuleId == "ref.dangling" && i.Location == "box-1");
            Assert.Contains(issues, i => i.RuleId == "ref.dangling" && i.Location == "i1");
            Assert.Contains(issues, i => i.RuleId == "ref.dangling" && i.Location == "phantom");
        }

        [Fact]
        public void UnknownScriptAndNodeKindAreErrors()
        {
            var draft = Good();
            draft.ReplaceRecoveryDevice(new RecoveryDevice("recovery-1", RecoveryDeviceKind.Forklift, new Cell(-1, 2), 1, "mystery", 3.7f));
            draft.Graphs.Add(new BehaviorGraph("g", null, new[] { new BehaviorNode("n", "openPassage") }, null));
            var issues = RoomValidator.ValidateDraft(draft, ValidationStage.Publish);
            Assert.Contains(issues, i => i.RuleId == "recovery.scriptUnknown" && i.Severity == Severity.Error);
            Assert.Contains(issues, i => i.RuleId == "behavior.nodeUnknown" && i.Location == "g/n");
        }

        [Fact]
        public void ImportedScriptAssetCountsAsKnownScript()
        {
            var draft = Good();
            draft.Assets.Add(new AssetDescriptor("script-1", AssetKinds.Script, "s", "h", ".lua"));
            draft.ReplaceRecoveryDevice(new RecoveryDevice("recovery-1", RecoveryDeviceKind.Forklift, new Cell(-1, 2), 1, "script-1", 3.7f));
            Assert.DoesNotContain(RoomValidator.ValidateDraft(draft, ValidationStage.Publish), i => i.RuleId == "recovery.scriptUnknown");
        }

        [Fact]
        public void ClosedOpenableCutsExitIsAWarningOnly()
        {
            var draft = Good();
            for (int y = 1; y <= 5; y++) draft.Room.SetTerrain(new Cell(4, y), TerrainCategories.Openable, "vine-" + y, TerrainCategories.Closed);
            draft.Room.RemoveObject("target-1");
            foreach (var stage in new[] { ValidationStage.Draft, ValidationStage.Publish })
            {
                var hit = RoomValidator.ValidateDraft(draft, stage).Single(i => i.RuleId == "openable.disconnected");
                Assert.Equal(Severity.Warning, hit.Severity);
                Assert.Equal("exit", hit.Location);
            }
        }

        [Fact]
        public void ConnectedRoomHasNoDisconnectWarning()
        {
            Assert.DoesNotContain(RoomValidator.ValidateDraft(Good(), ValidationStage.Publish), i => i.RuleId == "openable.disconnected");
        }

        [Fact]
        public void ModelFootprintRuleOnlyAcceptsOneByOne()
        {
            Assert.Null(RoomValidator.ModelFootprintIssue("m", 1f, 1.0005f));
            var bad = RoomValidator.ModelFootprintIssue("m", 1f, 1.5f);
            Assert.Equal(RoomValidator.ModelFootprintRuleId, bad.RuleId);
            Assert.Equal(Severity.Error, bad.Severity);
        }

        [Fact]
        public void DefaultRecoveryDurationIsThreePointSeven()
        {
            Assert.Equal(3.7f, RecoveryDevice.DefaultDuration);
        }

        [Fact]
        public void WorldDocumentHasNoContentVersion()
        {
            Assert.Null(typeof(WorldDocument).GetProperty("ContentVersion"));
            Assert.DoesNotContain("contentVersion", WorldCodec.Encode(new WorldDocument("w", new Cell(0, 0), new Cell(9, 9),
                new[] { new WorldRoomRef("a", "a.json", new Cell(0, 0), null) }, null, new WorldEndpoint("a", "spawn"), new WorldEndpoint("a", "exit"), null)));
        }
    }
}
