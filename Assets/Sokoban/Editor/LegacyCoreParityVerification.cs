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
                    var original = RoomFile.ToLegacyData(room);
                    var back = LegacyRoomWriter.FromDraft(LegacyRoomAdapter.ToDraft(original));
                    if (!original.RowsNorthToSouth.SequenceEqual(back.RowsNorthToSouth)) failures.Add(name + " 往返后地形行不一致");
                    if (!original.Doors.Select(d => d.Id + d.Cell + d.Direction + d.Role).OrderBy(s => s).SequenceEqual(back.Doors.Select(d => d.Id + d.Cell + d.Direction + d.Role).OrderBy(s => s))) failures.Add(name + " 往返后门不一致");
                    if (!original.Boxes.Select(b => b.Id + b.Cell + b.RecoveryCell).OrderBy(s => s).SequenceEqual(back.Boxes.Select(b => b.Id + b.Cell + b.RecoveryCell).OrderBy(s => s))) failures.Add(name + " 往返后箱子或恢复格不一致");
                    if (!original.Targets.Select(t => t.Id + t.Cell).OrderBy(s => s).SequenceEqual(back.Targets.Select(t => t.Id + t.Cell).OrderBy(s => s))) failures.Add(name + " 往返后目标不一致");
                    if ((original.Spawn?.Id + original.Spawn?.Cell) != (back.Spawn?.Id + back.Spawn?.Cell)) failures.Add(name + " 往返后出生点不一致");
                    var legacyDraft = new RoomDraft(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "World/" + name + ".room.json")));
                    for (int py = -1; py <= original.Height; py++)
                        for (int px = -1; px <= original.Width; px++)
                        {
                            string oldError = legacyDraft.LegacyPlacementError(true, new Vector2Int(px, py));
                            string routed = legacyDraft.PlacementError(true, new Vector2Int(px, py));
                            if (routed != oldError) failures.Add(name + " 转调后的 PlacementError 与旧判定不一致 (" + px + "," + py + ")：旧=" + oldError + " 转调=" + routed);
                            string newError = PlacementRules.Error(draft, ObjectCategories.Box, new Sokoban.Core.Cell(px, py), null);
                            if (oldError != newError) failures.Add(name + " 放置预检不一致 (" + px + "," + py + ")：旧=" + oldError + " 新=" + newError);
                        }
                    for (int dy = 0; dy < original.Height; dy++)
                        for (int dx = 0; dx < original.Width; dx++)
                        {
                            var dcell = new Vector2Int(dx, dy);
                            string oldDirection = legacyDraft.LegacyDoorDirection(dcell);
                            string newDirection = legacyDraft.DoorDirection(dcell);
                            if (oldDirection != newDirection) failures.Add(name + " 门朝向不一致 " + dcell + "：旧=" + oldDirection + " 新=" + newDirection);
                        }
                    var freeCell = Enumerable.Range(0, original.Width * original.Height).Select(i => new Vector2Int(i % original.Width, i / original.Width)).First(p => legacyDraft.PlacementError(true, p) == null);
                    legacyDraft.Place(true, freeCell, "parity-cache-probe");
                    string afterPlace = legacyDraft.PlacementError(true, freeCell);
                    if (afterPlace != "格子已占用") failures.Add(name + " 放置后缓存未失效 " + freeCell + "：" + afterPlace);
                    legacyDraft.Undo();
                    if (legacyDraft.PlacementError(true, freeCell) != null) failures.Add(name + " 撤销后缓存未失效 " + freeCell);
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
