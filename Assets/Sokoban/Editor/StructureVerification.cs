/**
 * @file StructureVerification.cs
 * CheckRecoverySource 验证无法读取的原草稿备份，以及相机视角读取错误不会丢弃可读草稿。
 * @brief Run 用实际房间和世界文件比较拒写前后的内容，再从草稿恢复并修复。
 * Require 检查逐项条件；Rule 统计 RoomValidator 发布级校验中指定规则的问题数；Same 逐房比较两个房间目录的 RoomCodec 文本。
 * With 改写快照中的某个房间或世界文档后新建草稿，用于构造门朝向错误与连接错误的样本，因为门与世界文档不可原地修改。
 * Reject 从草稿恢复异常样本，经文件面板尝试保存、导出与发布并核对原文件；快照为字符串，不含撤销历史。
 * Submit 经真实布局面板提交路径和覆盖选择；CheckOutputs 重新打开各入口写出的文件并比较房间内容。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Sokoban.Core;

namespace Sokoban.Editor
{
    public static class StructureVerification
    {
        static StreamWriter log;
        static string source;

        static void Require(bool condition, string message)
        {
            log.WriteLine((condition ? "PASS " : "FAIL ") + message);
            if (!condition) throw new InvalidOperationException(message);
        }

        static int Rule(RoomDraft draft, string ruleId) =>
            RoomFile.Issues(draft.Model, ValidationStage.Publish).Count(i => i.RuleId == ruleId && i.Severity == Severity.Error);

        static bool Same(System.Collections.Generic.IReadOnlyDictionary<string, RoomDraftModel> a, System.Collections.Generic.IReadOnlyDictionary<string, RoomDraftModel> b) =>
            a.Count == b.Count && a.All(pair => b.TryGetValue(pair.Key, out var other) && RoomCodec.Encode(pair.Value) == RoomCodec.Encode(other));

        static RoomDraft With(RoomDraft draft, string roomId, RoomDraftModel model, WorldDocument world)
        {
            var root = (Dictionary<string, object>)MiniJson.Parse(draft.Snapshot());
            if (model != null) ((Dictionary<string, object>)root["rooms"])[roomId] = RoomCodec.Encode(model);
            if (world != null) root["world"] = WorldCodec.Encode(world);
            var next = new RoomDraft(RoomCodec.Decode(source));
            next.Recover(MiniJson.Write(root));
            return next;
        }

        static RoomDraftModel WithDoors(RoomDraftModel src, IEnumerable<DoorInfo> doors)
        {
            var source = src.Room;
            var room = RoomResize.Resized(source, 0, 0, source.Width, source.Height);
            for (int x = 0; x < source.Width; x++)
                for (int y = 0; y < source.Height; y++)
                {
                    var look = source.AppearanceAt(new Cell(x, y));
                    if (look != null) room.SetAppearance(new Cell(x, y), look);
                }
            foreach (var placed in source.Objects)
                if (placed.Appearance != null) room.SetObjectAppearance(placed.Id, placed.Appearance);
            var next = new RoomDraftModel(room, doors, src.PrimaryEntranceId, src.Extras, src.RecoveryItems);
            next.Meta = src.Meta;
            next.Decorations.AddRange(src.Decorations);
            next.Assets.AddRange(src.Assets);
            next.Graphs.AddRange(src.Graphs);
            next.Instances.AddRange(src.Instances);
            foreach (var pair in src.Templates) next.Templates[pair.Key] = pair.Value;
            foreach (var pair in src.ObjectStates) next.ObjectStates[pair.Key] = pair.Value;
            next.UiImageAssetId = src.UiImageAssetId;
            return next;
        }

        static void Submit(LayoutPanel panel, string action, string path)
        {
            panel.ChooseFile(action);
            if (!panel.Modal) return;
            panel.Filename = Path.GetFullPath(path);
            panel.SubmitFile();
            if (panel.Overwrite && panel.Modal) panel.SubmitFile();
        }

        static void CheckOutputs(RoomDraft draft, string path)
        {
            var panel = new LayoutPanel();
            panel.Draft.Recover(draft.Snapshot());
            var actions = draft.World == null ? new[] { "保存", "导出" } : new[] { "保存", "导出", "发布" };
            foreach (var action in actions)
            {
                string output = action == "保存" ? path : Path.Combine(Path.GetDirectoryName(path), action + "-" + Path.GetFileName(path));
                Submit(panel, action, output);
                var reopened = new RoomDraft(RoomCodec.Decode(source));
                reopened.Open(output, false);
                Require(!panel.Modal && panel.Status.Contains("已" + action) && Same(reopened.Catalog, draft.Catalog),
                    action + "成功，重新打开房间目录与修复后的数据相同：" + Path.GetFileName(output));
            }
        }

        static string Differ(string a, string b)
        {
            int length = Math.Min(a.Length, b.Length), at = 0;
            while (at < length && a[at] == b[at]) at++;
            if (at == length && a.Length == b.Length) return "相同";
            int from = Math.Max(0, at - 40);
            return "位置 " + at + " 前 " + a.Substring(from, Math.Min(100, a.Length - from)) + " 后 " + b.Substring(from, Math.Min(100, b.Length - from));
        }

        static void Reject(RoomDraft draft, string path, string expected)
        {
            string original = File.ReadAllText(path);
            string snapshot = draft.Snapshot();
            string directory = Path.Combine(Path.GetDirectoryName(path), "错误草稿", expected.Trim());
            var writer = new LayoutPanel("structure", directory);
            writer.Draft.Recover(snapshot);
            Require(writer.Flush(), "错误编辑现场完整写入独立磁盘草稿");
            var panel = new LayoutPanel("structure", directory);
            string restored = panel.Draft.Snapshot();
            Require(snapshot == restored, "从磁盘恢复错误布局：" + panel.Status);
            var actions = draft.World == null ? new[] { "保存", "导出" } : new[] { "保存", "导出", "发布" };
            foreach (string action in actions)
            {
                Submit(panel, action, path);
                Require(panel.Status.Contains(expected), action + "拒绝提示包含原因：" + panel.Status);
                Require(File.ReadAllText(path) == original, action + "拒绝后原文件内容不变");
                string after = panel.Draft.Snapshot();
                Require(restored == after, action + "拒绝后草稿快照不变：" + Differ(restored, after));
                Require(true, action + "拒绝并保留原文件与草稿：" + panel.Status);
                panel.CancelDialog();
            }
        }

        static string Ready(string text)
        {
            var draft = new RoomDraft(RoomCodec.Decode(text));
            draft.Place(true, new Cell(3, 4));
            draft.Place(false, new Cell(4, 4));
            VerificationFixtures.EnsureRecovery(draft);
            return RoomCodec.Encode(draft.Model);
        }

        static void CheckRecoverySource(string evidence)
        {
            string directory = Path.Combine(evidence, "recovery-source");
            Directory.CreateDirectory(directory);
            string slot = Path.Combine(directory, "unreadable-ws02.json");
            const string legacy = "{\"assetOffset\":0.125}";
            File.WriteAllText(slot, legacy);
            var panel = new LayoutPanel("unreadable", directory);
            var backup = Directory.GetFiles(directory, "unreadable-ws02.json.unreadable-*").Single();
            Require(panel.Flush() && File.ReadAllText(backup) == legacy && File.ReadAllText(slot) != legacy,
                "旧草稿读失败后保存新恢复槽，原文件按字节保留");

            string viewPath = slot + ".view";
            File.WriteAllText(viewPath, "invalid");
            string snapshot = File.ReadAllText(slot);
            var reopened = new LayoutPanel("unreadable", directory);
            Require(reopened.Draft.Snapshot() == snapshot && reopened.Status.Contains("视角读取失败"),
                "异常视角文件不替换可读草稿");
        }

        [MenuItem("Tools/Sokoban/验证/编辑体验结构检查")]
        public static void Run()
        {
            string evidence = Path.Combine("Docs/验收证据/WS-02编辑体验补充", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(evidence);
            using (log = new StreamWriter(Path.Combine(evidence, "结构检查.txt")) { AutoFlush = true })
            {
                CheckRecoverySource(evidence);
                source = Ready(Resources.Load<TextAsset>("DefaultRoom").text);
                var draft = new RoomDraft(RoomCodec.Decode(source));
                string path = Path.Combine(evidence, "sample.room.json");
                draft.Save(path, false);
                var gaps = new[] { new Cell(0, 0), new Cell(2, 6) };
                Require(draft.Brush(gaps, RoomTool.Floor) == null, "两格墙改地板的整笔笔划合法并提交");
                Require(Rule(draft, "boundary.open") == 2, "boundary.open 定位角墙与边墙两个缺口，每格一条");
                Reject(draft, path, "边界缺墙");
                var slot = Path.Combine(evidence, "invalid-draft.json");
                File.WriteAllText(slot, draft.Snapshot());
                draft = new RoomDraft(RoomCodec.Decode(source));
                draft.Recover(File.ReadAllText(slot));
                Require(Rule(draft, "boundary.open") == 2, "错误草稿重开保留缺口");
                bool undone = true;
                try { draft.Undo(); } catch (Exception) { undone = false; }
                Require(!undone, "快照不含撤销历史，重开后撤销被拒绝");
                Require(draft.Brush(gaps, RoomTool.Wall) == null && Rule(draft, "boundary.open") == 0, "用墙笔重新补回两格，缺口消失");
                draft.Save(path, false);
                draft.Open(path, false);
                Require(Rule(draft, "boundary.open") == 0 && Rule(draft, "door.geometry") == 0, "补墙后已有门通过，修复后重开");
                CheckOutputs(draft, path);
                draft.Open("Docs/关卡样例/main.world.json", true);
                string world = Path.Combine(evidence, "three.world.json");
                draft.Save(world, false);
                draft.UseRoom("factory");
                var links = draft.World.Connections.ToList();
                var candidate = new Cell(40, 10);
                Require(WorldLayout.ConnectionIssues(draft.World, draft.Catalog, "factory", candidate).Count() == 2, "候选原点将破坏两条直连");
                draft.PlaceRoom(candidate, true);
                Reject(draft, world, "直连门");
                Require(draft.World.Connections.SequenceEqual(links), "拒写保留全部连接字段");
                draft.Undo();
                Require(draft.Brush(new[] { new Cell(0, 0) }, RoomTool.Floor) == null, "世界内房间的角墙改地板笔划合法");
                Reject(draft, world, "factory");
                draft.Undo();
                var entry = draft.Model.Doors.First(d => d.Id == "entry");
                var flipped = draft.Model.Doors.Select(d => d.Id == "entry" ? new DoorInfo(d.Id, d.Cell, new Cell(-d.Outward.X, -d.Outward.Y), d.Role) : d).ToList();
                var badDoor = With(draft, "factory", WithDoors(draft.Model, flipped), null);
                Require(RoomFile.Issues(badDoor.Model, ValidationStage.Publish).Any(i => i.RuleId == "door.geometry" && i.Location == entry.Id), "门朝向反转触发 door.geometry");
                Reject(badDoor, world, "门朝向不可通行");
                var first = draft.World.Connections[0];
                draft.RemoveConnection(first.Id);
                Require(!draft.World.Connections.Any(link => link.Id == first.Id), "共享命令解除门连接");
                draft.AddConnection(first.From, first.To, WorldCodec.Teleport, false);
                var edited = draft.World.Connections.Last();
                Require(edited.Mode == WorldCodec.Teleport && !edited.AllowReturn, "共享命令新建单向传送");
                draft.Save(world, false);
                var reopened = new RoomDraft(RoomCodec.Decode(source));
                reopened.Open(world, false);
                var restoredLink = reopened.World.Connections.Single(link => link.Id == edited.Id);
                Require(restoredLink.Mode == WorldCodec.Teleport && !restoredLink.AllowReturn, "保存重开保留传送及回退设置");
                draft.Undo();
                draft.Undo();
                Require(draft.World.Connections.Any(link => link.Id == first.Id), "撤销两步恢复原直连");
                var duplicate = new WorldConnection("duplicate-endpoint", first.Mode, first.From, first.To, first.AllowReturn);
                var repeated = With(draft, null, null, WorldLayout.WithConnections(draft.World, draft.World.Connections.Concat(new[] { duplicate }).ToList()));
                Reject(repeated, world, "重复使用");
                var broken = new WorldConnection(first.Id, first.Mode, new WorldEndpoint(first.From.LevelId, "missing"), first.To, first.AllowReturn);
                var missing = With(draft, null, null, WorldLayout.WithConnections(draft.World, draft.World.Connections.Select(c => c.Id == first.Id ? broken : c).ToList()));
                Reject(missing, world, "缺失");
                draft.Save(world, false);
                draft.Open(world, false);
                Require(!WorldLayout.Issues(draft.World, draft.Catalog).Any(), "三房修复后可保存并完整重开");
                CheckOutputs(draft, world);
            }
        }
    }
}
