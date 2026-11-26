/**
 * @file LegacyCoreParityVerification.cs
 * @brief Run 读取三个正式房间，用旧 RoomFile.BoundaryGaps 与 Core 的 boundary.open 比较缺口格集合，并要求 Core 的 door.geometry 不报错；不一致时逐条列出并以退出码 1 结束。
 * Run：批处理入口，-executeMethod Sokoban.Editor.LegacyCoreParityVerification.Run。
 * Gaps：把 Core 校验结果里的 boundary.open 定位还原成格集合，便于与旧实现逐格对照。
 */
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Sokoban.Core;

namespace Sokoban.Editor
{
    public static class LegacyCoreParityVerification
    {
        static readonly string[] Rooms = { "warehouse", "factory", "forest" };

        static HashSet<Vector2Int> Gaps(IEnumerable<Issue> issues) =>
            new HashSet<Vector2Int>(issues.Where(i => i.RuleId == "boundary.open").Select(i =>
            {
                var parts = i.Location.Trim('(', ')').Split(',');
                return new Vector2Int(int.Parse(parts[0]), int.Parse(parts[1]));
            }));

        public static void Run()
        {
            var failures = new List<string>();
            try
            {
                foreach (var name in Rooms)
                {
                    var room = RoomFile.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "World/" + name + ".room.json")), true);
                    var draft = LegacyRoomAdapter.ToDraft(RoomFile.ToLegacyData(room));
                    foreach (var item in RecoveryMapping.FromPresentation(RoomFile.RecoveryPreset(room), draft)) new AddRecoveryItemCommand(draft, item).Apply();
                    var issues = RoomValidator.ValidateDraft(draft, ValidationStage.Publish);
                    var legacy = new HashSet<Vector2Int>(RoomFile.LegacyBoundaryGaps(room));
                    var core = Gaps(issues);
                    foreach (var cell in legacy.Except(core)) failures.Add(name + " 旧实现有缺口而 Core 没有：" + cell);
                    foreach (var cell in core.Except(legacy)) failures.Add(name + " Core 有缺口而旧实现没有：" + cell);
                    foreach (var issue in issues.Where(i => i.Severity == Severity.Error))
                        failures.Add(name + " Core 发布级错误：" + issue.RuleId + " " + issue.Location + " " + issue.Message);
                    foreach (var issue in issues.Where(i => i.RuleId.StartsWith("box.recovery")))
                        failures.Add(name + " Core 恢复格报错：" + issue.Location + " " + issue.Message);
                    foreach (var issue in issues.Where(i => i.RuleId == "door.geometry"))
                        failures.Add(name + " Core 门几何报错：" + issue.Location + " " + issue.Message);
                    foreach (var issue in issues.Where(i => i.Severity == Severity.Error))
                        Debug.Log("PARITY BASELINE " + name + " 发布级错误 " + issue.RuleId + " " + issue.Location + " " + issue.Message);
                    Debug.Log("PARITY " + name + " legacyGaps=" + legacy.Count + " coreGaps=" + core.Count + " publishErrors=" + issues.Count(i => i.Severity == Severity.Error));
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                failures.Add("异常：" + ex.Message);
            }
            foreach (var line in failures) Debug.LogError("PARITY FAIL " + line);
            if (failures.Count == 0) Debug.Log("PARITY PASS");
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }
    }
}
