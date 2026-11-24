/**
 * @file StructureVerification.cs
 * @brief Run 用实际房间和世界文件比较拒写前后的内容，再从草稿恢复并修复。
 * Require 检查逐项条件；Reject 从草稿恢复异常样本，经文件面板尝试保存、导出与发布并核对原文件。
 * Submit 经真实布局面板提交路径和覆盖选择；CheckOutputs 重新打开各入口写出的文件并比较房间内容。
 */
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Sokoban.Editor
{
    public static class StructureVerification
    {
        static StreamWriter log;

        static void Require(bool condition, string message)
        {
            log.WriteLine((condition ? "PASS " : "FAIL ") + message);
            if (!condition) throw new InvalidOperationException(message);
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
                var reopened = new RoomDraft(Resources.Load<TextAsset>("DefaultRoom").text);
                reopened.Open(output, false);
                Require(!panel.Modal && panel.Status.Contains("已" + action) && JToken.DeepEquals(reopened.Catalog, draft.Catalog),
                    action + "成功，重新打开房间目录与修复后的数据相同：" + Path.GetFileName(output));
            }
        }

        static void Reject(RoomDraft draft, string path, string expected)
        {
            string original = File.ReadAllText(path);
            var snapshot = draft.Snapshot();
            string directory = Path.Combine(Path.GetDirectoryName(path), "错误草稿", expected.Trim());
            var writer = new LayoutPanel("structure", directory);
            writer.Draft.Recover((JObject)snapshot.DeepClone());
            Require(writer.Flush(), "错误编辑现场完整写入独立磁盘草稿");
            var panel = new LayoutPanel("structure", directory);
            var restored = panel.Draft.Snapshot();
            Require(JToken.DeepEquals(JObject.Parse(snapshot.ToString()), JObject.Parse(restored.ToString())),
                "从磁盘恢复错误布局及历史：" + panel.Status);
            var actions = draft.World == null ? new[] { "保存", "导出" } : new[] { "保存", "导出", "发布" };
            foreach (string action in actions)
            {
                Submit(panel, action, path);
                Require(panel.Status.Contains(expected) && File.ReadAllText(path) == original && JToken.DeepEquals(restored, panel.Draft.Snapshot()),
                    action + "拒绝并保留原文件、草稿及历史：" + panel.Status);
                panel.CancelDialog();
            }
        }

        [MenuItem("Tools/Sokoban/验证/编辑体验结构检查")]
        public static void Run()
        {
            string evidence = Path.Combine("Docs/验收证据/WS-02编辑体验补充", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(evidence);
            using (log = new StreamWriter(Path.Combine(evidence, "结构检查.txt")) { AutoFlush = true })
            {
                string source = Resources.Load<TextAsset>("DefaultRoom").text;
                var draft = new RoomDraft(source);
                string path = Path.Combine(evidence, "sample.room.json");
                draft.Save(path, false);
                var gaps = new[] { Vector2Int.zero, new Vector2Int(2, 6) };
                draft.Brush(gaps, RoomTool.Floor);
                Require(RoomFile.StructureIssues(draft.Room).Count() == 2, "定位普通边墙及角墙两个缺口");
                Reject(draft, path, "边界缺墙");
                var slot = Path.Combine(evidence, "invalid-draft.json");
                RoomFile.Write(slot, draft.Snapshot().ToString());
                draft = new RoomDraft(source);
                draft.Recover(JObject.Parse(File.ReadAllText(slot)));
                Require(RoomFile.StructureIssues(draft.Room).Count() == 2, "错误草稿重开保留缺口");
                draft.Undo();
                draft.Save(path, false);
                draft.Open(path, false);
                Require(!RoomFile.StructureIssues(draft.Room).Any(), "补回两块边墙，已有门通过，修复后重开");
                CheckOutputs(draft, path);
                draft.Open("Docs/关卡样例/main.world.json", true);
                string world = Path.Combine(evidence, "three.world.json");
                draft.Save(world, false);
                draft.UseRoom("factory");
                var links = draft.World["connections"].DeepClone();
                var candidate = new Vector2Int(40, 10);
                Require(WorldLayout.ConnectionIssues(draft.World, draft.Catalog, "factory", candidate).Count() == 2,
                    "候选原点将破坏两条直连");
                draft.PlaceRoom(candidate, true);
                Reject(draft, world, "直连门");
                Require(JToken.DeepEquals(links, draft.World["connections"]), "拒写保留全部连接字段");
                draft.Undo();
                draft.Brush(new[] { Vector2Int.zero }, RoomTool.Floor);
                Reject(draft, world, "factory");
                draft.Undo();
                var link = (JObject)draft.World["connections"][0];
                var door = draft.Room["doors"].First(d => (string)d["id"] == "entry");
                string direction = (string)door["direction"];
                door["direction"] = direction == "W" ? "E" : "W";
                Reject(draft, world, "需要朝外");
                door["direction"] = direction;
                var duplicate = (JObject)link.DeepClone();
                duplicate["id"] = "duplicate-endpoint";
                ((JArray)draft.World["connections"]).Add(duplicate);
                Reject(draft, world, "重复使用");
                duplicate.Remove();
                link["forwardFrom"]["doorId"] = "missing";
                Reject(draft, world, "missing");
                link["forwardFrom"]["doorId"] = "exit";
                draft.Save(world, false);
                draft.Open(world, false);
                Require(!WorldLayout.Issues(draft.World, draft.Catalog).Any(), "三房修复后可保存并完整重开");
                CheckOutputs(draft, world);
            }
        }
    }
}
