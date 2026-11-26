/**
 * @file LegacyCoreParityVerification.cs
 * @brief Run 读取三个正式房间，用旧 RoomFile.BoundaryGaps 与 Core 的 boundary.open 比较缺口格集合，并要求 Core 的 door.geometry 不报错；不一致时逐条列出并以退出码 1 结束。
 * Run：批处理入口，-executeMethod Sokoban.Editor.LegacyCoreParityVerification.Run。
 * Gaps：把 Core 校验结果里的 boundary.open 定位还原成格集合，便于与旧实现逐格对照。
 * BridgeChecks：对三个正式房间原样与七种破坏用例（出生点移除、两箱恢复格重复、两对象同格、对象在墙内、重复 ID、主入口内侧放箱子、门朝向非法），比较旧 RoomFile.StructureIssues 前半段与 RoomCoreBridge.StructureIssues 的条数和首条文本，并比较 RoomCoreBridge.LoadIssues 是否非空与旧 Parse 草稿读入是否抛 InvalidDataException。
 * OldFront：枚举旧 StructureIssues 并只保留缺出生点、恢复位置重复、门朝外、主入口内侧留空、找不到主入口五类文案；这五类都在边界缺口等后半段之前产出，后半段抛出的异常（如门朝向非法时 ToDraft 抛 FormatException）被吞掉，只保留已产出的前半段。
 * NewFront：对桥接层同样收集，异常类型名记为一条。
 * OldThrows：以 RoomFile.Parse(json, true) 触发旧 Validate，捕获 InvalidDataException。
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

        static string RoomJson(string name) => File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "World/" + name + ".room.json"));

        static Newtonsoft.Json.Linq.JToken WallCell(Newtonsoft.Json.Linq.JObject r)
        {
            var rows = (Newtonsoft.Json.Linq.JArray)r["terrainRowsNorthToSouth"];
            for (int row = 0; row < rows.Count; row++)
            {
                int x = ((string)rows[row]).IndexOf('#');
                if (x >= 0) return RoomFile.Point(new Vector2Int(x, rows.Count - 1 - row));
            }
            return RoomFile.Point(new Vector2Int(-1, -1));
        }

        static List<string> OldFront(Newtonsoft.Json.Linq.JObject room)
        {
            var list = new List<string>();
            try
            {
                foreach (var line in RoomFile.LegacyStructureIssues(room))
                    if (line.Contains("需要放置玩家出生点") || line.Contains("恢复位置重复") || line.Contains("需要朝外且内侧可通行") || line.Contains("主入口内侧需要留空") || line.Contains("找不到主入口")) list.Add(line);
            }
            catch (Exception) { }
            return list;
        }

        static List<string> NewFront(Newtonsoft.Json.Linq.JObject room)
        {
            var list = new List<string>();
            try { list.AddRange(RoomCoreBridge.StructureIssues(room)); }
            catch (Exception e) { list.Add("异常 " + e.GetType().Name); }
            return list;
        }

        static string OldThrows(string json)
        {
            try { RoomFile.Parse(json, true); return null; }
            catch (Exception e) { return e.GetType().Name; }
        }

        static void EnsureTwoBoxes(Newtonsoft.Json.Linq.JObject room)
        {
            var boxes = (Newtonsoft.Json.Linq.JArray)room["boxes"];
            if (boxes.Count >= 2) return;
            var taken = new HashSet<Vector2Int>(RoomFile.Objects(room).Select(RoomFile.At));
            foreach (var box in boxes) taken.Add(RoomFile.Cell(box["recoveryCell"]));
            var size = RoomFile.Size(room);
            for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!RoomFile.Floor(room, cell) || taken.Contains(cell)) continue;
                    if (room["doors"].Any(d => RoomFile.At(d) + RoomFile.Inward(d) == cell)) continue;
                    boxes.Add(new Newtonsoft.Json.Linq.JObject { ["id"] = "bridge-box-2", ["cell"] = RoomFile.Point(cell), ["recoveryCell"] = RoomFile.Point(cell) });
                    return;
                }
        }

        static void BridgeChecks(List<string> failures)
        {
            var cases = new List<(string, Action<Newtonsoft.Json.Linq.JObject>)>
            {
                ("原样", r => { }),
                ("出生点移除", r => r.Remove("spawn")),
                ("两箱恢复格重复", r => r["boxes"][1]["recoveryCell"] = r["boxes"][0]["recoveryCell"].DeepClone()),
                ("两对象同格", r => r["boxes"][1]["cell"] = r["boxes"][0]["cell"].DeepClone()),
                ("对象在墙内", r => r["boxes"][0]["cell"] = WallCell(r)),
                ("重复ID", r => r["boxes"][1]["id"] = r["boxes"][0]["id"].DeepClone()),
                ("主入口内侧放箱子", r => { var door = r["doors"].First(d => (string)d["id"] == (string)r["primaryEntranceId"]); r["boxes"][0]["cell"] = RoomFile.Point(RoomFile.At(door) + RoomFile.Inward(door)); }),
                ("门朝向非法", r => r["doors"][0]["direction"] = "X"),
            };
            int compared = 0;
            foreach (var name in Rooms)
                foreach (var (label, mutate) in cases)
                {
                    string tag = "桥接比对 " + name + " " + label;
                    var room = RoomFile.Parse(RoomJson(name), true);
                    EnsureTwoBoxes(room);
                    try { mutate(room); }
                    catch (Exception e) { failures.Add(tag + " 构造破坏用例失败：" + e.Message); continue; }
                    var oldList = OldFront(room);
                    var newList = NewFront(room);
                    if (oldList.Count != newList.Count) failures.Add(tag + " 结构问题条数不一致：旧=" + oldList.Count + " 桥接=" + newList.Count + " 旧首条=" + oldList.FirstOrDefault() + " 桥接首条=" + newList.FirstOrDefault());
                    else if (oldList.Count > 0 && oldList[0] != newList[0]) failures.Add(tag + " 结构问题首条文本不一致：旧=" + oldList[0] + " 桥接=" + newList[0]);
                    string thrown = OldThrows(room.ToString(Newtonsoft.Json.Formatting.None));
                    var load = RoomCoreBridge.LoadIssues(room).ToList();
                    if ((thrown != null) != (load.Count > 0)) failures.Add(tag + " LoadIssues 与旧 Validate 不一致：旧=" + (thrown ?? "不抛") + " 桥接=" + load.Count + " 条 " + load.FirstOrDefault());
                    else if (thrown != null && thrown != "InvalidDataException") failures.Add(tag + " 旧 Parse 抛出了非 InvalidDataException：" + thrown);
                    compared++;
                    Debug.Log("PARITY BRIDGE " + tag + " 旧条数=" + oldList.Count + " 桥接条数=" + newList.Count + " 旧读入=" + (thrown ?? "不抛") + " 桥接读入条数=" + load.Count);
                }
            Debug.Log("PARITY BRIDGE 比对用例数=" + compared);
        }

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
                    foreach (var property in room.Properties())
                    {
                        string text = property.Value.ToString(Newtonsoft.Json.Formatting.None);
                        bool structural = new[] { "levelId", "size", "terrainRowsNorthToSouth", "doors", "primaryEntranceId", "boxes", "targets", "spawn", "decorations", "formatVersion", "kind" }.Contains(property.Name);
                        if (!structural && (!original.Extras.TryGetValue(property.Name, out var kept) || kept != text)) failures.Add(name + " Extras 丢失顶层键 " + property.Name);
                    }
                    foreach (var box in room["boxes"])
                        if (!original.Extras.TryGetValue("box:" + (string)box["id"], out var boxText) || boxText != box.ToString(Newtonsoft.Json.Formatting.None)) failures.Add(name + " Extras 丢失箱子原文 " + (string)box["id"]);
                    foreach (var door in room["doors"])
                        if (!original.Extras.TryGetValue("door:" + (string)door["id"], out var doorText) || doorText != door.ToString(Newtonsoft.Json.Formatting.None)) failures.Add(name + " Extras 丢失门原文 " + (string)door["id"]);
                    var roundTrip = LegacyRoomWriter.FromDraft(LegacyRoomAdapter.ToDraft(original));
                    if (roundTrip.Extras.Count != original.Extras.Count || original.Extras.Any(pair => !roundTrip.Extras.TryGetValue(pair.Key, out var value) || value != pair.Value)) failures.Add(name + " Extras 经 Core 往返后不一致");
                    Debug.Log("PARITY EXTRAS " + name + " 键数=" + original.Extras.Count);
                    var back = LegacyRoomWriter.FromDraft(LegacyRoomAdapter.ToDraft(original));
                    if (!original.RowsNorthToSouth.SequenceEqual(back.RowsNorthToSouth)) failures.Add(name + " 往返后地形行不一致");
                    if (!original.Doors.Select(d => d.Id + d.Cell + d.Direction + d.Role).OrderBy(s => s).SequenceEqual(back.Doors.Select(d => d.Id + d.Cell + d.Direction + d.Role).OrderBy(s => s))) failures.Add(name + " 往返后门不一致");
                    if (!original.Boxes.Select(b => b.Id + b.Cell).OrderBy(s => s).SequenceEqual(back.Boxes.Select(b => b.Id + b.Cell).OrderBy(s => s))) failures.Add(name + " 往返后箱子不一致");
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
                    for (int ny = 0; ny < original.Height; ny++)
                        for (int nx = 0; nx < original.Width; nx++)
                        {
                            var ncell = new Vector2Int(nx, ny);
                            bool oldFloor = RoomDraft.LegacyNeedsFloor(legacyDraft.Room, ncell);
                            bool routedFloor = legacyDraft.NeedsFloor(legacyDraft.Room, ncell);
                            if (oldFloor != routedFloor) failures.Add(name + " NeedsFloor 转调与旧判定不一致 " + ncell + "：旧=" + oldFloor + " 转调=" + routedFloor);
                        }
                    for (int dy = 0; dy < original.Height; dy++)
                        for (int dx = 0; dx < original.Width; dx++)
                        {
                            var dcell = new Vector2Int(dx, dy);
                            string oldDirection = legacyDraft.LegacyDoorDirection(dcell);
                            string newDirection = legacyDraft.DoorDirection(dcell);
                            if (oldDirection != newDirection) failures.Add(name + " 门朝向不一致 " + dcell + "：旧=" + oldDirection + " 新=" + newDirection);
                        }
                    int markerDiffs = 0;
                    for (int my = 0; my < original.Height; my++)
                        for (int mx = 0; mx < original.Width; mx++)
                        {
                            var mcell = new Vector2Int(mx, my);
                            if (!RoomFile.Floor(legacyDraft.Room, mcell)) continue;
                            var spawnItem = legacyDraft.Room["spawn"];
                            bool oldOccupied = legacyDraft.LegacyMarkerOccupied(mcell, spawnItem);
                            bool coreOccupied = legacyDraft.MarkerOccupied(mcell, spawnItem);
                            if (oldOccupied != coreOccupied)
                            {
                                markerDiffs++;
                                failures.Add(name + " 标记占用判定不一致 " + mcell + "：旧=" + oldOccupied + " 新=" + coreOccupied);
                            }
                        }
                    Debug.Log("PARITY MARKER-PROBE " + name + " 差异格数=" + markerDiffs);
                    foreach (var moving in RoomFile.Objects(legacyDraft.Room).Where(o => legacyDraft.Room["boxes"].Contains(o) || legacyDraft.Room["targets"].Contains(o)).ToList())
                    {
                        string movingId = (string)moving["id"];
                        bool movingBox = legacyDraft.Room["boxes"].Contains(moving);
                        for (int vy = 0; vy < original.Height; vy++)
                            for (int vx = 0; vx < original.Width; vx++)
                            {
                                var vcell = new Vector2Int(vx, vy);
                                if (legacyDraft.PlacementError(movingBox, vcell, movingId) != null) continue;
                                var moveDraft = LegacyRoomAdapter.ToDraft(RoomFile.ToLegacyData(legacyDraft.Room));
                                new MoveObjectCommand(moveDraft, movingId, new Sokoban.Core.Cell(vx, vy)).Apply();
                                var moved = moveDraft.Room.Objects.First(o => o.Id == movingId);
                                var viaCore = moved.Cell.ToString();
                                var expected = new Sokoban.Core.Cell(vx, vy).ToString();
                                if (viaCore != expected) failures.Add(name + " 移动命令结果与旧 Move 不一致 " + movingId + " -> " + vcell + "：Core=" + viaCore + " 旧=" + expected);
                            }
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
                BridgeChecks(failures);
                var worldPath = Path.Combine(Application.streamingAssetsPath, "World/main.world.json");
                var catalog = new Newtonsoft.Json.Linq.JObject();
                var legacyWorld = WorldLayout.Read(worldPath, catalog);
                var coreWorld = WorldCodec.Decode(File.ReadAllText(worldPath));
                var sizes = new Dictionary<string, Sokoban.Core.Cell>();
                foreach (var roomRef in coreWorld.Rooms)
                {
                    var roomSize = RoomFile.Size((Newtonsoft.Json.Linq.JObject)catalog[roomRef.LevelId]);
                    sizes[roomRef.LevelId] = new Sokoban.Core.Cell(roomSize.x, roomSize.y);
                }
                int worldChecks = 0;
                foreach (var roomRef in coreWorld.Rooms)
                {
                    var size = sizes[roomRef.LevelId];
                    for (int wy = coreWorld.Min.Y - 3; wy <= coreWorld.Max.Y + 3; wy++)
                        for (int wx = coreWorld.Min.X - 3; wx <= coreWorld.Max.X + 3; wx++)
                            foreach (bool moving in new[] { true, false })
                            {
                                string oldIssue = WorldLayout.PlacementIssue(legacyWorld, catalog, roomRef.LevelId, new Vector2Int(wx, wy), moving);
                                string newIssue = WorldValidator.Placement(coreWorld, sizes, roomRef.LevelId, new Sokoban.Core.Cell(wx, wy), moving, size);
                                worldChecks++;
                                if (oldIssue != newIssue) failures.Add("世界放置检查不一致 " + roomRef.LevelId + " (" + wx + "," + wy + ") moving=" + moving + "：旧=" + oldIssue + " 新=" + newIssue);
                            }
                }
                Debug.Log("PARITY WORLD-PLACEMENT 比对点数=" + worldChecks);
                var coreRooms = WorldLayout.ToCoreRooms(catalog);
                int linkChecks = 0;
                string Join(IEnumerable<string> lines) => string.Join(" | ", lines);
                string oldBase = Join(WorldLayout.LegacyConnectionIssues(legacyWorld, catalog));
                string routedBase = Join(WorldLayout.ConnectionIssues(legacyWorld, catalog));
                if (routedBase != oldBase) failures.Add("转调后的 ConnectionIssues 与旧判定不一致（原样）：旧=" + oldBase + " 转调=" + routedBase);
                string newBase = Join(WorldValidator.Connections(coreWorld, coreRooms));
                linkChecks++;
                if (oldBase != newBase) failures.Add("世界连接检查不一致（原样）：旧=" + oldBase + " 新=" + newBase);
                foreach (var roomRef in coreWorld.Rooms)
                    for (int lx = -2; lx <= 24; lx++)
                        for (int ly = -1; ly <= 3; ly++)
                        {
                            string oldMove = Join(WorldLayout.LegacyConnectionIssues(legacyWorld, catalog, roomRef.LevelId, new Vector2Int(lx, ly)));
                            string routedMove = Join(WorldLayout.ConnectionIssues(legacyWorld, catalog, roomRef.LevelId, new Vector2Int(lx, ly)));
                            if (routedMove != oldMove) failures.Add("转调后的 ConnectionIssues 平移预演不一致 " + roomRef.LevelId + " (" + lx + "," + ly + ")：旧=" + oldMove + " 转调=" + routedMove);
                            string newMove = Join(WorldValidator.Connections(coreWorld, coreRooms, roomRef.LevelId, new Sokoban.Core.Cell(lx, ly)));
                            linkChecks++;
                            if (oldMove != newMove) failures.Add("世界连接平移预演不一致 " + roomRef.LevelId + " (" + lx + "," + ly + ")：旧=" + oldMove + " 新=" + newMove);
                        }
                var oldEnds = WorldLayout.Issues(legacyWorld, catalog).Where(i => i.StartsWith("世界起点") || i.StartsWith("世界终点门")).ToList();
                var newEnds = WorldValidator.Endpoints(coreWorld, coreRooms).ToList();
                if (oldEnds.Count != newEnds.Count) failures.Add("起终点检查条数不一致：旧=" + oldEnds.Count + " 新=" + newEnds.Count);
                var legacyEnds = WorldLayout.LegacyEndpointIssues(legacyWorld, catalog).ToList();
                var routedEnds = WorldLayout.EndpointIssues(legacyWorld, catalog);
                if (legacyEnds.Count != routedEnds.Count) failures.Add("转调后的起终点检查条数不一致：旧=" + legacyEnds.Count + " 转调=" + routedEnds.Count);
                foreach (var bad in new[] { "spawnId", "doorId" })
                {
                    var broken = (Newtonsoft.Json.Linq.JObject)legacyWorld.DeepClone();
                    if (bad == "spawnId") broken["start"]["spawnId"] = "nowhere"; else broken["finish"]["endpoint"]["doorId"] = "nowhere";
                    int oldCount = WorldLayout.LegacyEndpointIssues(broken, catalog).Count();
                    int newCount = WorldLayout.EndpointIssues(broken, catalog).Count;
                    if (oldCount != 1 || newCount != 1) failures.Add("破坏 " + bad + " 后起终点问题条数异常：旧=" + oldCount + " 转调=" + newCount);
                }
                Debug.Log("PARITY WORLD-LINKS 比对点数=" + linkChecks + " 起终点问题=" + newEnds.Count);
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
