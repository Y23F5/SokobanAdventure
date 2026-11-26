/**
 * @file TerrainExpansionVerification.cs
 * @brief Run 从菜单或批处理逐例执行共享命令并汇总失败。Fixture 构造带地形身份与独立恢复格的世界。
 * Expansion 核对尺寸、原点、旧格与身份；Conflict 检查冲突整笔回滚；History 检查草稿与双向历史；
 * Blank 从已有世界新建空房；BlankState 和 BlankRoundTrip 核对空白状态与草稿往返；BlankSave 验证正式保存拒绝。
 * Repave 擦除再铺墙并撤销重做；OldRoom 从磁盘重开旧房逐字段核对。
 * BuildAndReopen 从空房铺设走廊与门、保存并逐笔核对历史；ProtectedPreview 和 Conflict 分别检查保护格与世界预检。
 * ExpandSolo 向西南扩一格；CheckSoloModels/SoloModels 核对模型世界坐标及历史；SoloAutoRecovery 通过 Tick 自动写槽后重开。
 * SoloExport 导出并检查临时原点；SoloTrial 验证单房试玩前的结构拒绝及修复。
 * Erase 检查对象、地形及受保护格。Require 报告断言，Paint 调用共享笔刷并检查返回值。VerifyModelBrushes 用输入 GLB 检查分类放置和交换文件。
 */
using System;
using System.Linq;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class TerrainExpansionVerification
    {
        static readonly Vector2Int Start = new Vector2Int(10, 10);
        static readonly Vector2Int BoundCell = new Vector2Int(2, 4);

        static RoomDraft Fixture()
        {
            var room = JObject.Parse(Resources.Load<TextAsset>("DefaultRoom").text);
            room["boxes"][0]["recoveryCell"] = new JArray(3, 4);
            room["terrainObjects"] = new JArray(new JObject { ["id"] = "floor-a", ["cell"] = RoomFile.Point(BoundCell) });
            room["events"] = new JArray(new JObject { ["id"] = "graph-a", ["ownerId"] = "floor-a", ["nodes"] = new JArray(), ["edges"] = new JArray() });
            var draft = new RoomDraft(room.ToString());
            draft.BeginWorld();
            draft.PlaceRoom(Start, false);
            return draft;
        }

        static void Require(bool ok, string detail)
        {
            if (!ok) throw new InvalidOperationException(detail);
        }

        static void Paint(RoomDraft draft, Vector2Int point, RoomTool tool)
        {
            var issue = draft.Brush(new[] { point }, tool);
            Require(issue == null, $"{point} {tool} {issue}");
        }

        static void Expansion(Vector2Int point, Vector2Int shift, Vector2Int growth, RoomTool tool)
        {
            var draft = Fixture();
            var before = (JObject)draft.Room.DeepClone();
            var size = RoomFile.Size(before);
            Paint(draft, point, tool);
            Require(draft.Origin == Start + shift && RoomFile.Size(draft.Room) == size + growth, "扩房原点或尺寸错误");
            for (int y = 0; y < size.y + growth.y; y++)
            for (int x = 0; x < size.x + growth.x; x++)
            {
                var local = new Vector2Int(x, y);
                var old = local + shift;
                char expected = old == point ? (tool == RoomTool.Wall ? '#' : '.') : RoomFile.Terrain(before, old);
                Require(RoomFile.Terrain(draft.Room, local) == expected, $"地形行偏移 {local}");
            }
            foreach (var item in RoomFile.Objects(before).Concat(before["terrainObjects"]))
            {
                var id = (string)item["id"];
                var actual = RoomFile.Objects(draft.Room).Concat(draft.Room["terrainObjects"]).Single(o => (string)o["id"] == id);
                Require(draft.Origin + RoomFile.At(actual) == Start + RoomFile.At(item), id + " 世界格改变");
            }
            Require(draft.Origin + RoomFile.Cell(draft.Find("box-a")["recoveryCell"]) == Start + new Vector2Int(3, 4), "恢复格漂移");
            Require(JToken.DeepEquals(draft.Room["events"], before["events"]), "地形绑定改变");
        }

        static void Conflict(bool overlap, bool preview = false)
        {
            var draft = Fixture();
            var point = new Vector2Int(-11, 2);
            if (overlap)
            {
                string first = draft.ActiveId;
                var other = (JObject)draft.Room.DeepClone();
                other["levelId"] = "neighbor";
                draft.Catalog["neighbor"] = other;
                draft.UseRoom("neighbor");
                draft.PlaceRoom(Start + new Vector2Int(7, 0), false);
                draft.UseRoom(first);
                point = new Vector2Int(7, 2);
            }
            var before = draft.Snapshot();
            if (preview)
            {
                foreach (var tool in new[] { RoomTool.Floor, RoomTool.Wall })
                {
                    string error = draft.TerrainPlacementError(point, tool);
                    Require(error != null && error.Contains(overlap ? "重叠" : "边界") && error.Contains(overlap ? "(17, 10)" : "(-1, 10)"), "预检未定位冲突 " + error);
                    Require(draft.TerrainPlacementError(new Vector2Int(3, 7), tool) == null, "预检误拒空闲扩建");
                }
                Require(JToken.DeepEquals(before, draft.Snapshot()), "预检改变历史");
                return;
            }
            var issue = draft.Brush(new[] { BoundCell, point }, RoomTool.Wall);
            Require(issue != null && issue.Contains(overlap ? "重叠" : "边界"), "未报告预期冲突 " + issue);
            Require(JToken.DeepEquals(before, draft.Snapshot()), "冲突改变工作区或历史");
        }

        static void History()
        {
            var draft = Fixture();
            var before = draft.Snapshot()["current"];
            string issue = draft.Brush(new[] { new Vector2Int(-1, 2), new Vector2Int(1, -1) }, RoomTool.Floor);
            Require(issue == null && draft.Origin == Start - Vector2Int.one, "西南复合笔失败 " + issue);
            var after = draft.Snapshot()["current"];
            draft.Undo();
            Require(JToken.DeepEquals(before, draft.Snapshot()["current"]), "一笔撤销未恢复房间与世界");
            var snapshot = draft.Snapshot();
            var restored = Fixture();
            restored.Recover(JObject.Parse(snapshot.ToString()));
            var recovered = JObject.Parse(restored.Snapshot().ToString());
            restored.Redo();
            Require(JToken.DeepEquals(JToken.Parse(after.ToString()), JToken.Parse(restored.Snapshot()["current"].ToString())), "草稿重做丢失扩房结果");
            restored.Undo();
            Require(JToken.DeepEquals(JToken.Parse(before.ToString()), JToken.Parse(restored.Snapshot()["current"].ToString())), "草稿再次撤销失败");
            Require(JToken.DeepEquals(JObject.Parse(snapshot.ToString()), recovered), "序列化草稿改变");
        }

        static void Erase(bool bound)
        {
            var draft = Fixture();
            if (bound)
            {
                var before = draft.Snapshot()["current"];
                Paint(draft, BoundCell, RoomTool.Erase);
                Require(RoomFile.Terrain(draft.Room, BoundCell) == RoomFile.Void, "擦除后仍有地形");
                bool retainedAfterErase = (draft.Room["terrainObjects"] as JArray ?? new JArray()).Any(o => (string)o["id"] == "floor-a");
                draft.Undo();
                Require(JToken.DeepEquals(before, draft.Snapshot()["current"]), "撤销擦除未恢复原身份");
                draft.Redo();
                Paint(draft, BoundCell, RoomTool.Floor);
                bool retainedAfterRepaint = (draft.Room["terrainObjects"] as JArray ?? new JArray()).Any(o => (string)o["id"] == "floor-a");
                Require(!retainedAfterErase && !retainedAfterRepaint, $"旧身份 floor-a 存在于空格={retainedAfterErase}，重绘后={retainedAfterRepaint}");
                return;
            }
            var initial = draft.Snapshot();
            var blocked = draft.Brush(new[] { new Vector2Int(3, 4) }, RoomTool.Erase);
            Require(blocked != null && JToken.DeepEquals(initial, draft.Snapshot()), "擦掉箱子恢复格");
            var cell = RoomFile.At(draft.Find("box-a"));
            var beforeErase = draft.Snapshot();
            var refused = draft.Brush(new[] { cell }, RoomTool.Erase);
            Require(refused != null && JToken.DeepEquals(beforeErase, draft.Snapshot()) && draft.Find("box-a") != null, "擦除跨对象格应整笔拒绝且草稿不变");
            var free = Enumerable.Range(0, RoomFile.Size(draft.Room).x * RoomFile.Size(draft.Room).y)
                .Select(i => new Vector2Int(i % RoomFile.Size(draft.Room).x, i / RoomFile.Size(draft.Room).x))
                .First(p => draft.PlacementError(true, p, "box-a") == null);
            draft.Select(cell);
            draft.Tool = RoomTool.Move;
            draft.Move(free);
            Require(draft.Find("box-a") != null && RoomFile.At(draft.Find("box-a")) == free && RoomFile.Floor(draft.Room, cell), "箱子未能移出原格");
            Paint(draft, cell, RoomTool.Erase);
            Require(RoomFile.Terrain(draft.Room, cell) == RoomFile.Void, "对象移走后擦除地板失败");
        }

        static RoomDraft Blank()
        {
            var draft = Fixture();
            draft.NewBlankRoom();
            return draft;
        }

        static void BlankState()
        {
            var draft = Blank();
            var rows = (JArray)draft.Room["terrainRowsNorthToSouth"];
            Require(RoomFile.Size(draft.Room) == new Vector2Int(15,15) && rows.Count == 15 && rows.All(row => (string)row == new string(RoomFile.Void,15)), "新房尺寸或空格错误");
            Require(!RoomFile.Objects(draft.Room).Any() && !draft.Room["events"].Any() && draft.Room["primaryEntranceId"] == null, "空房残留对象或主入口");
            Require(draft.World == null && draft.WorkPath == null && draft.Dirty && draft.Catalog.Count == 1 && draft.ActiveId != "ws01-room", "新建未重置旧工作区");
        }

        static void BlankRoundTrip()
        {
            var draft = Blank();
            var snapshot = JObject.Parse(draft.Snapshot().ToString());
            var restored = Fixture();
            restored.Recover(snapshot);
            Require(restored.Dirty && JToken.DeepEquals(snapshot, JObject.Parse(restored.Snapshot().ToString())), "空白草稿往返丢失状态");
        }

        static void BlankSave()
        {
            var draft = Blank();
            var before = draft.Snapshot();
            string path = Path.Combine(Application.temporaryCachePath, Guid.NewGuid().ToString("N") + ".room.json");
            try
            {
                draft.Save(path, false);
                throw new InvalidOperationException("正式保存接受了空白草稿");
            }
            catch (InvalidDataException ex) when (ex.Message.Contains("出生点"))
            {
                Require(!File.Exists(path) && JToken.DeepEquals(before, draft.Snapshot()), "保存拒绝后文件或草稿被修改");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        static void Repave()
        {
            var draft = Blank();
            var cell = new Vector2Int(7,7);
            Paint(draft, cell, RoomTool.Floor);
            Require(RoomFile.Floor(draft.Room, cell), "空格铺地失败");
            Paint(draft, cell, RoomTool.Erase);
            Require(RoomFile.Terrain(draft.Room, cell) == RoomFile.Void, "地板未擦除");
            Paint(draft, cell, RoomTool.Wall);
            Require(RoomFile.Terrain(draft.Room, cell) == '#' && RoomFile.Terrain(draft.Room, cell + Vector2Int.right) == RoomFile.Void, "重铺墙影响错误格");
            draft.Undo();
            Require(RoomFile.Terrain(draft.Room, cell) == RoomFile.Void, "撤销重铺未恢复空格");
            draft.Redo();
            Require(RoomFile.Terrain(draft.Room, cell) == '#', "重做未恢复墙");
        }

        static void OldRoom()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Docs/关卡样例/WS-01.json"));
            var expected = JObject.Parse(File.ReadAllText(path));
            var draft = Blank();
            draft.Open(path, false);
            Require(JToken.DeepEquals(expected, draft.Room) && !draft.Dirty && draft.WorkPath == path, "既有房间数据或文件关联改变");
            Require(draft.Find("spawn") != null && draft.Find("box-a") != null && draft.Find("entry") != null, "旧房关键对象缺失");
        }

        static void ProtectedPreview()
        {
            var draft = Fixture();
            var before = draft.Snapshot();
            foreach (var cell in new[] { new Vector2Int(2,2), new Vector2Int(3,2), new Vector2Int(1,3), new Vector2Int(3,4) })
            {
                var error = draft.TerrainPlacementError(cell, RoomTool.Wall);
                Require(error != null && error.Contains("占用") && error.Contains(cell.ToString()), "保护格预检错误 " + cell);
                Require(draft.TerrainPlacementError(cell, RoomTool.Floor) == null, "保护格误拒地板 " + cell);
            }
            Require(draft.TerrainPlacementError(new Vector2Int(4,4), RoomTool.Wall) == null, "普通格误拒墙");
            Require(JToken.DeepEquals(before, draft.Snapshot()), "保护格预检改变草稿");
        }

        static void BuildAndReopen()
        {
            var draft = Blank();
            var states = new System.Collections.Generic.List<JToken> { draft.Room.DeepClone() };
            var corridor = Enumerable.Range(3, 5).Select(x => new Vector2Int(x, 7)).ToArray();
            Require(draft.Brush(corridor, RoomTool.Floor) == null, "铺设走廊失败");
            states.Add(draft.Room.DeepClone());
            Require(draft.Brush(corridor.SelectMany(c => new[] { c + Vector2Int.up, c + Vector2Int.down }), RoomTool.Wall) == null, "封墙失败");
            states.Add(draft.Room.DeepClone());
            draft.SetRoomMarker(corridor[0], "entry");
            states.Add(draft.Room.DeepClone());
            draft.SetRoomMarker(corridor[2], "spawn");
            states.Add(draft.Room.DeepClone());
            draft.SetRoomMarker(corridor[4], "exit");
            states.Add(draft.Room.DeepClone());
            var entry = draft.Room["doors"].Single(d => (string)d["role"] == "entry");
            var exit = draft.Room["doors"].Single(d => (string)d["role"] == "exit");
            Require(RoomFile.At(draft.Room["spawn"]) == corridor[1] && RoomFile.At(entry) == corridor[0] && RoomFile.At(exit) == corridor[4], "标记坐标错误");
            Require((string)entry["direction"] == "W" && (string)exit["direction"] == "E" && (string)draft.Room["primaryEntranceId"] == (string)entry["id"], "门朝向或主入口错误");
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/WS05A-" + draft.ActiveId + ".room.json"));
            draft.Save(path, false);
            string contents = File.ReadAllText(path);
            var publicDesign = RoomFile.ForExport((JObject)states.Last());
            Require((int)publicDesign["formatVersion"] == 3 && !draft.Dirty && draft.WorkPath == path &&
                JToken.DeepEquals(JObject.Parse(contents), publicDesign), "正式文件或保存状态错误");
            for (int i = states.Count - 2; i >= 0; i--)
            {
                draft.Undo();
                Require(draft.Dirty && JToken.DeepEquals(draft.Room, states[i]), "撤销步骤错误 " + i);
            }
            for (int i = 1; i < states.Count; i++)
            {
                draft.Redo();
                Require(JToken.DeepEquals(draft.Room, states[i]), "重做步骤错误 " + i);
            }
            Require(!draft.Dirty && File.ReadAllText(path) == contents, "重做后保存基线或磁盘改变");
            draft.Open(path, false);
            var reopened = draft.Snapshot();
            Require(!draft.Dirty && draft.WorkPath == path && JToken.DeepEquals(draft.Room, publicDesign), "正式文件重读不一致");
            Require(!reopened["undo"].Any() && !reopened["redo"].Any(), "重开继承旧编辑历史");
            Debug.Log("WS05A ROOM " + path);
        }

        static void ExpandSolo(RoomDraft draft)
        {
            Require(draft.World == null && draft.Origin == Vector2Int.zero, "独立房间初始条件错误");
            Paint(draft, new Vector2Int(-1,-1), RoomTool.Wall);
            Require(draft.Origin == new Vector2Int(-1,-1), "西南扩建未移动临时原点");
        }

        static void CheckSoloModels(RoomPreview preview, RoomDraft draft, JObject original)
        {
            preview.Sync(draft);
            var group = GameObject.Find("Room • " + draft.ActiveId).transform.Find(draft.ActiveId);
            void At(string name, Vector2Int world)
            {
                var model = group.Find(name);
                Require(model && model.GetComponentInChildren<Renderer>() && model.position == new Vector3(world.x, 0, world.y), "模型世界位置错误 " + name);
            }
            foreach (var item in RoomFile.Objects(original)) At((string)item["id"], RoomFile.At(item));
            var size = RoomFile.Size(original);
            for (int y = 0; y < size.y; y++)
            for (int x = 0; x < size.x; x++)
            {
                var world = new Vector2Int(x, y);
                string kind = RoomFile.Floor(original, world) ? "Floor " : "Wall ";
                At(kind + (world - draft.Origin), world);
            }
        }

        static void SoloModels()
        {
            var draft = new LayoutPanel().Draft;
            var original = (JObject)draft.Room.DeepClone();
            var preview = new RoomPreview();
            try
            {
                CheckSoloModels(preview, draft, original);
                ExpandSolo(draft);
                var expanded = draft.Room.DeepClone();
                CheckSoloModels(preview, draft, original);
                draft.Undo();
                Require(draft.Origin == Vector2Int.zero && JToken.DeepEquals(original,draft.Room), "撤销未恢复原点或布局");
                CheckSoloModels(preview, draft, original);
                draft.Redo();
                Require(draft.Origin == -Vector2Int.one && JToken.DeepEquals(expanded,draft.Room), "重做未恢复扩建");
                CheckSoloModels(preview, draft, original);
            }
            finally { preview.Clear(); }
        }

        static void SoloAutoRecovery()
        {
            string folder = Path.Combine(Application.dataPath,"../Logs/solo-" + Guid.NewGuid().ToString("N"));
            var panel = new LayoutPanel("terrain",folder);
            ExpandSolo(panel.Draft);
            string path = Path.Combine(folder,"terrain-ws02.json");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!File.Exists(path) && timer.ElapsedMilliseconds < 5000)
            {
                panel.Tick();
                System.Threading.Thread.Sleep(25);
            }
            Require(File.Exists(path), "自动草稿未落盘");
            var restored = new LayoutPanel("terrain",folder).Draft;
            Require(restored.Origin == -Vector2Int.one && JToken.DeepEquals(restored.Room,panel.Draft.Room), "跨会话草稿未恢复原点或布局");
            restored.Undo(); Require(restored.Origin == Vector2Int.zero, "恢复后撤销原点错误");
            restored.Redo(); Require(restored.Origin == -Vector2Int.one, "恢复后重做原点错误");
            Debug.Log("WS05A SLOT " + Path.GetFullPath(path));
        }

        static void SoloExport()
        {
            var draft = new LayoutPanel().Draft;
            ExpandSolo(draft);
            var expected = RoomFile.ForExport(draft.Room);
            string path = Path.GetFullPath(Path.Combine(Application.dataPath,"../Logs/solo-" + Guid.NewGuid().ToString("N") + ".room.json"));
            draft.Save(path,true);
            var file = JObject.Parse(File.ReadAllText(path));
            Require(file["editorOrigin"] == null && JToken.DeepEquals(expected,file) && draft.Origin == -Vector2Int.one, "导出泄漏临时原点或改变布局");
            draft.Open(path,false);
            Require(draft.Origin == Vector2Int.zero && JToken.DeepEquals(expected,draft.Room), "独立正式房间重开后原点或局部布局错误");
            Debug.Log("WS05A EXPORT " + path);
        }

        static void SoloTrial()
        {
            var draft = new LayoutPanel().Draft;
            ExpandSolo(draft);
            var run = PuzzleRun.FromDraft(draft);
            Require(run.Origin == Vector2Int.zero && run.Player == RoomFile.At(draft.Room["spawn"]), "合法单房试玩坐标错误");
            var wall = new Vector2Int(3,0) - draft.Origin;
            Require(RoomFile.Terrain(draft.Room, wall) == '#', "样例南墙缺失");
            Paint(draft, wall, RoomTool.Erase);
            Require(RoomFile.BoundaryGaps(draft.Room).Contains(wall + Vector2Int.up), "测试未形成地板开口");
            var before = draft.Snapshot();
            try { PuzzleRun.FromDraft(draft); throw new InvalidOperationException("试玩接受了缺墙房间"); }
            catch (InvalidDataException ex) when (ex.Message.Contains("边界缺墙")) { }
            Require(JToken.DeepEquals(before,draft.Snapshot()), "拒绝试玩改变了草稿");
            draft.Undo();
            Require(PuzzleRun.FromDraft(draft).Player == run.Player, "修复后试玩出生位置错误");
            draft.NewBlankRoom();
            try { PuzzleRun.FromDraft(draft); throw new InvalidOperationException("试玩接受了无出生点空房"); }
            catch (InvalidDataException ex) when (ex.Message.Contains("出生点")) { }
        }

        public static void VerifyModelBrushes()
        {
            var draft = new RoomDraft(Resources.Load<TextAsset>("DefaultRoom").text);
            var path = Environment.GetEnvironmentVariable("SOKOBAN_BRUSH_MODEL");
            var output = Environment.GetEnvironmentVariable("SOKOBAN_BRUSH_OUTPUT");
            foreach (var category in new[] { RoomTool.Wall, RoomTool.Floor, RoomTool.Box, RoomTool.Target })
            {
                draft.Tool = category;
                var model = draft.ImportAsset(path, "model");
                draft.SelectAsset(model, category);
                draft.Recover(draft.Snapshot());
                Require(draft.BrushAsset == model && draft.Tool == category, "草稿还原分类笔刷");
                var before = draft.Room.DeepClone();
                var cell = category == RoomTool.Target ? new Vector2Int(4,4) : new Vector2Int(2,4);
                if (category == RoomTool.Wall || category == RoomTool.Floor)
                    Require(draft.Brush(new[] { cell, cell + Vector2Int.up }, category) == null, "连续铺设");
                else draft.Click(cell);
                var placed = draft.Room.DeepClone();
                string collection = category == RoomTool.Box ? "boxes" : category == RoomTool.Target ? "targets" : "terrainObjects";
                var item = placed[collection].Single(entity => RoomFile.At(entity) == cell);
                Require((string)item["visual"]["model"] == model, "放置类型与模型");
                draft.Undo(); Require(JToken.DeepEquals(draft.Room,before), "整笔撤销");
                draft.Redo(); Require(JToken.DeepEquals(draft.Room,placed), "整笔重做");
                Debug.Log("LOOK BRUSH PASS " + category);
            }
            draft.Save(output, true);
            var reopened = RoomAssets.ReadRoom(output);
            Require((int)reopened["formatVersion"] == 3 && ((JObject)reopened["appearanceCatalog"]).Count == 4 && ((JObject)reopened["assets"]).Count == 1, "四类共用一份资源并重读");
            Debug.Log("LOOK BRUSH PASS exchange");
        }

        [MenuItem("Sokoban/验证/WS-05A 共享地形与扩建")]
        public static void Run()
        {
            int failed = 0;
            int total = 0;
            void Test(string name, Action check)
            {
                total++;
                try { check(); Debug.Log("WS05A PASS " + name); }
                catch (Exception ex) { failed++; Debug.LogError("WS05A FAIL " + name + " " + ex); }
            }
            Test("east", () => Expansion(new Vector2Int(7, 1), Vector2Int.zero, Vector2Int.right, RoomTool.Floor));
            Test("west", () => Expansion(new Vector2Int(-1, 1), Vector2Int.left, Vector2Int.right, RoomTool.Wall));
            Test("north", () => Expansion(new Vector2Int(1, 7), Vector2Int.zero, Vector2Int.up, RoomTool.Wall));
            Test("south", () => Expansion(new Vector2Int(1, -1), Vector2Int.down, Vector2Int.up, RoomTool.Floor));
            Test("world-boundary", () => Conflict(false));
            Test("world-overlap", () => Conflict(true));
            Test("history-draft", History);
            Test("erase-object-recovery", () => Erase(false));
            Test("erase-bound-terrain", () => Erase(true));
            Test("blank-state", BlankState);
            Test("blank-draft-roundtrip", BlankRoundTrip);
            Test("blank-formal-save-refused", BlankSave);
            Test("blank-repave", Repave);
            Test("blank-open-old-room", OldRoom);
            Test("blank-build-save-history-reopen", BuildAndReopen);
            Test("preview-world-boundary", () => Conflict(false, true));
            Test("preview-world-overlap", () => Conflict(true, true));
            Test("preview-protected-cells", ProtectedPreview);
            Test("solo-preview-origin-history", SoloModels);
            Test("solo-auto-slot-origin-history", SoloAutoRecovery);
            Test("solo-export-reopen-origin", SoloExport);
            Test("solo-trial-structure", SoloTrial);
            Debug.Log($"WS05A RESULT {total - failed}/{total}");
            Require(failed == 0, $"WS05A {failed} failed");
        }
    }
}
