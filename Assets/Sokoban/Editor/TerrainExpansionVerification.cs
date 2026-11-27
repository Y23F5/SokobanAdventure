/**
 * @file TerrainExpansionVerification.cs
 * @brief 地形笔刷与扩建的编辑器回归：用 Core 的 BrushPlanner、BrushCommand、CommandHistory 与 RoomDraft 的命令语义核对整笔预检、扩建平移与撤销、擦除不删对象、门格受保护、世界视图越界整笔拒绝、单独编辑扩建后世界不自动补偿、空白房间与保存重开。
 * Run：菜单与批处理入口，逐例执行并汇总失败。
 * Fixture、FixtureDraft：由图纸构造带门、出生点、箱、目标与恢复装置的 9×5 房间。
 * Expansion：四个方向的扩建，核对尺寸、旧格地形、对象与门的平移，以及一步撤销重做。
 * WholeStroke、DoorsProtected、EraseKeepsObjects：整笔拒绝不留历史、门格对三种工具受保护、擦除只改地形。
 * WorldViewRejected、WorldPreview、StandaloneOverlap：世界视图越界整笔拒绝与预检文案，单独编辑扩建后返回世界报重叠且可撤销。
 * History、StandaloneOrigin：复合扩建一步撤销重做，临时原点随命令历史联动。
 * BlankState、BlankRoundTrip、BlankSave、Repave、FormalRoomReopen、BuildAndReopen、ProtectedPreview：空白房间、草稿快照往返、正式保存拒绝、重铺、正式房间重开、从空房铺设保存重开与保护格预检。
 * AutoSlot、Export、Trial：草稿槽自动落盘与跨会话恢复、导出不泄漏临时原点、扩建后试玩的结构拒绝与修复。
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
    public static class TerrainExpansionVerification
    {
        const string WorldRefusal = "请进入单独编辑后再扩建";
        static readonly string[] FixtureRows = { "#########", "#.......#", "<.P.B.T.>", "#.......#", "#########" };

        static string WorldPath() => Path.Combine(Application.streamingAssetsPath, "World/main.world.json");
        static string WarehousePath() => Path.Combine(Application.streamingAssetsPath, "World/warehouse.room.json");

        static RoomDraftModel Fixture()
        {
            var parsed = Blueprint.Parse("qa-expand", FixtureRows);
            var model = new RoomDraftModel(parsed.Room, parsed.Doors, "entry");
            RecoveryDeviceCommands.Place(new DraftHolder(model), new RecoveryDevice("recovery-device", RecoveryDeviceKind.Forklift, new Cell(-1, 2), 1, "warehouse-lift", 3.7f)).Apply();
            return model;
        }

        static RoomDraft FixtureDraft() => new RoomDraft(Fixture());

        static RoomDraft WorldDraft()
        {
            var draft = FixtureDraft();
            draft.Open(WorldPath(), false);
            Require(draft.World != null && draft.ActiveId == "warehouse", "正式世界未作为活动房间 warehouse 打开");
            return draft;
        }

        static void Require(bool ok, string detail)
        {
            if (!ok) throw new InvalidOperationException(detail);
        }

        static string Terrain(RoomDraft draft, Cell cell) => draft.Room.TerrainAt(cell).CategoryId;

        static void Paint(RoomDraft draft, Cell cell, RoomTool tool)
        {
            var issue = draft.Brush(new[] { cell }, tool);
            Require(issue == null, $"{cell} {tool} {issue}");
        }

        static void Expansion(Cell point, Cell shift, Cell growth, BrushTool tool, string target)
        {
            var holder = new DraftHolder(Fixture());
            var before = holder.Draft;
            string text = RoomCodec.Encode(before);
            var history = new CommandHistory();
            var plan = BrushPlanner.Preflight(before, new[] { point }, tool);
            Require(plan.Error == null && plan.Expands, "扩建预检失败 " + plan.Error);
            Require(plan.Left == shift.X && plan.Bottom == shift.Y, "平移量错误 " + plan.Left + "," + plan.Bottom);
            history.Execute(plan.CreateCommand(holder));
            var after = holder.Draft;
            Require(after.Room.Width == before.Room.Width + growth.X && after.Room.Height == before.Room.Height + growth.Y, "扩房尺寸错误");
            Require(after.Room.TerrainAt(point + shift).CategoryId == target, "新格地形错误 " + point);
            for (int y = 0; y < before.Room.Height; y++)
                for (int x = 0; x < before.Room.Width; x++)
                {
                    var old = new Cell(x, y);
                    Require(after.Room.TerrainAt(old + shift).CategoryId == before.Room.TerrainAt(old).CategoryId, "旧格地形偏移 " + old);
                }
            foreach (var o in before.Room.Objects)
                Require(after.Room.Objects.Single(a => a.Id == o.Id).Cell == o.Cell + shift, o.Id + " 格坐标未随平移");
            foreach (var door in before.Doors)
            {
                var moved = after.Doors.Single(d => d.Id == door.Id);
                var terrain = after.Room.TerrainAt(moved.Cell);
                Require(moved.Cell == door.Cell + shift && moved.Outward == door.Outward && terrain.CategoryId == TerrainCategories.Door && terrain.EntityId == door.Id, door.Id + " 门未随平移");
            }
            var device = after.RecoveryDevice;
            Require(after.PrimaryEntranceId == before.PrimaryEntranceId && device != null && device.Id == before.RecoveryDevice.Id &&
                device.Kind == before.RecoveryDevice.Kind && device.Anchor == new Cell(-1, before.RecoveryDevice.Anchor.Y + shift.Y) &&
                device.Rotation == before.RecoveryDevice.Rotation && device.ScriptRef == before.RecoveryDevice.ScriptRef &&
                device.Duration == before.RecoveryDevice.Duration, "主入口或恢复装置改变");
            Require(history.Undo() && ReferenceEquals(holder.Draft, before) && RoomCodec.Encode(holder.Draft) == text, "撤销未还原扩建");
            Require(history.Redo() && holder.Draft.Room.Width == after.Room.Width && holder.Draft.Room.Height == after.Room.Height, "重做未恢复扩建");
        }

        static void WholeStroke()
        {
            var holder = new DraftHolder(Fixture());
            string text = RoomCodec.Encode(holder.Draft);
            var history = new CommandHistory();
            var legal = new Cell(3, 1);
            var rejected = new[]
            {
                (new[] { legal, new Cell(0, 2) }, "门格"),
                (new[] { legal, new Cell(4, 2) }, "占用"),
                (new[] { legal, new Cell(1, 2) }, "占用"),
            };
            foreach (var (cells, word) in rejected)
            {
                var plan = BrushPlanner.Preflight(holder.Draft, cells, BrushTool.Wall);
                Require(plan.Error != null && plan.Error.Contains(word), "整笔预检未拒绝 " + plan.Error);
                try { plan.CreateCommand(holder); throw new InvalidOperationException("整笔被拒绝后仍生成命令"); }
                catch (InvalidOperationException ex) when (ex.Message == plan.Error) { }
                try { history.Execute(new BrushCommand(holder, cells, BrushTool.Wall)); throw new InvalidOperationException("非法笔划被提交"); }
                catch (InvalidOperationException ex) when (ex.Message == plan.Error) { }
                Require(history.Revision == 0 && !history.CanUndo && RoomCodec.Encode(holder.Draft) == text && holder.Draft.Room.TerrainAt(legal).CategoryId == TerrainCategories.Floor, "整笔拒绝改变了草稿或历史");
            }
        }

        static void DoorsProtected()
        {
            var draft = Fixture();
            foreach (var tool in new[] { BrushTool.Wall, BrushTool.Floor, BrushTool.Erase })
                foreach (var door in draft.Doors)
                {
                    var plan = BrushPlanner.Preflight(draft, new[] { door.Cell }, tool);
                    Require(plan.Error != null && plan.Error.Contains("门格"), $"{tool} 未保护门格 {door.Id} {plan.Error}");
                }
        }

        static void EraseKeepsObjects()
        {
            var holder = new DraftHolder(Fixture());
            var history = new CommandHistory();
            var boxCell = new Cell(4, 2);
            var blocked = BrushPlanner.Preflight(holder.Draft, new[] { boxCell }, BrushTool.Erase);
            Require(blocked.Error != null && blocked.Error.Contains("占用") && holder.Draft.Room.Objects.Count == 3, "擦掉箱子所在地板应整笔拒绝且对象不删");
            var outside = BrushPlanner.Preflight(holder.Draft, new[] { new Cell(-1, 0) }, BrushTool.Erase);
            Require(outside.Error != null && outside.Error.Contains("擦除不能扩建"), "擦除越界应拒绝而不是扩建");
            history.Execute(new MoveObjectCommand(holder.Draft, "box-1", new Cell(4, 3)));
            var plan = BrushPlanner.Preflight(holder.Draft, new[] { boxCell }, BrushTool.Erase);
            Require(plan.Error == null && !plan.NoChange, "对象移走后擦除地板失败 " + plan.Error);
            history.Execute(plan.CreateCommand(holder));
            Require(holder.Draft.Room.TerrainAt(boxCell).CategoryId == TerrainCategories.Void && holder.Draft.Room.Objects.Count == 3 && holder.Draft.Room.Objects.Single(o => o.Id == "box-1").Cell == new Cell(4, 3), "擦除只应改地形，对象仍在");
            history.Undo();
            Require(holder.Draft.Room.TerrainAt(boxCell).CategoryId == TerrainCategories.Floor, "撤销擦除未还原地板");
        }

        static void WorldViewRejected()
        {
            var draft = WorldDraft();
            string before = draft.Snapshot();
            int revision = draft.Revision;
            var legal = new Cell(3, 1);
            Require(Terrain(draft, legal) == TerrainCategories.Floor, "测试格应为地板");
            foreach (var tool in new[] { RoomTool.Wall, RoomTool.Floor })
                foreach (var outside in new[] { new Cell(9, 1), new Cell(-1, 1), new Cell(3, 7), new Cell(3, -1) })
                {
                    var issue = draft.Brush(new[] { legal, outside }, tool);
                    Require(issue == WorldRefusal, $"世界视图越界应整笔拒绝 {tool} {outside} {issue}");
                }
            Require(draft.Brush(new[] { legal, new Cell(-1, 1) }, RoomTool.Erase) != null, "世界视图擦除越界应整笔拒绝");
            Require(Terrain(draft, legal) == TerrainCategories.Floor && draft.Revision == revision && !draft.Dirty && before == draft.Snapshot(), "越界整笔拒绝改变了草稿");
        }

        static void WorldPreview()
        {
            var draft = WorldDraft();
            string before = draft.Snapshot();
            foreach (var tool in new[] { RoomTool.Floor, RoomTool.Wall })
            {
                Require(draft.TerrainPlacementError(new Cell(-1, 1), tool) == WorldRefusal, "预检未提示进入单独编辑 " + tool);
                Require(draft.TerrainPlacementError(new Cell(3, 3), tool) == null, "预检误拒房内空闲格 " + tool);
            }
            Require(before == draft.Snapshot(), "预检改变草稿");
        }

        static void StandaloneOverlap()
        {
            var draft = WorldDraft();
            var baseline = WorldLayout.Issues(draft.World, draft.Catalog).ToList();
            Require(baseline.Count == 0, "正式世界应无布局问题：" + string.Join("；", baseline));
            draft.EnterStandaloneEdit("warehouse");
            Require(draft.IsStandalone && draft.World == null && draft.Origin == new Cell(0, 0), "单独编辑初始条件错误");
            Paint(draft, new Cell(9, 1), RoomTool.Floor);
            Require(draft.Room.Width == 10, "单独编辑扩建失败");
            var issues = draft.ReturnToWorld();
            Require(!draft.IsStandalone && draft.World != null && issues.Any(i => i.Contains("重叠")), "返回世界后应报房间重叠：" + string.Join("；", issues));
            Require(draft.Room.Width == 10, "世界不应自动补偿房间尺寸");
            draft.Undo();
            Require(draft.Room.Width == 9 && WorldLayout.Issues(draft.World, draft.Catalog).Count() == 0, "撤销写回后世界应恢复无问题");
        }

        static void History()
        {
            var draft = FixtureDraft();
            string before = RoomCodec.Encode(draft.Model);
            string issue = draft.Brush(new[] { new Cell(-1, 2), new Cell(1, -1) }, RoomTool.Floor);
            Require(issue == null && draft.Origin == new Cell(-1, -1), "西南复合笔失败 " + issue);
            string after = RoomCodec.Encode(draft.Model);
            draft.Undo();
            Require(before == RoomCodec.Encode(draft.Model) && draft.Origin == new Cell(0, 0), "一笔撤销未恢复房间与原点");
            draft.Redo();
            Require(after == RoomCodec.Encode(draft.Model) && draft.Origin == new Cell(-1, -1), "重做丢失扩房结果");
            draft.Undo();
            string snapshot = draft.Snapshot();
            var restored = FixtureDraft();
            restored.Recover(snapshot);
            Require(restored.Snapshot() == snapshot, "序列化草稿改变");
        }

        static void StandaloneOrigin()
        {
            var draft = FixtureDraft();
            var originals = draft.Room.Objects.ToDictionary(o => o.Id, o => o.Cell);
            string before = RoomCodec.Encode(draft.Model);
            Require(draft.World == null && draft.Origin == new Cell(0, 0), "独立房间初始条件错误");
            Paint(draft, new Cell(-1, -1), RoomTool.Wall);
            Require(draft.Origin == new Cell(-1, -1), "西南扩建未移动临时原点");
            foreach (var o in draft.Room.Objects) Require(draft.Origin + o.Cell == originals[o.Id], o.Id + " 世界格改变");
            string expanded = RoomCodec.Encode(draft.Model);
            draft.Undo();
            Require(draft.Origin == new Cell(0, 0) && RoomCodec.Encode(draft.Model) == before, "撤销未恢复原点或布局");
            draft.Redo();
            Require(draft.Origin == new Cell(-1, -1) && RoomCodec.Encode(draft.Model) == expanded, "重做未恢复扩建");
        }

        static void BlankState()
        {
            var draft = FixtureDraft();
            draft.NewBlankRoom();
            bool allVoid = true;
            for (int x = 0; x < draft.Room.Width; x++)
                for (int y = 0; y < draft.Room.Height; y++) allVoid &= Terrain(draft, new Cell(x, y)) == TerrainCategories.Void;
            Require(draft.Room.Width == 15 && draft.Room.Height == 15 && allVoid, "新房尺寸或空格错误");
            Require(draft.Room.Objects.Count == 0 && draft.Model.Doors.Count == 0 && draft.Model.PrimaryEntranceId == null &&
                draft.Model.RecoveryDevice != null && draft.Model.RecoveryDevice.Kind == RecoveryDeviceKind.Forklift &&
                draft.Model.RecoveryDevice.Anchor == new Cell(-1, 7), "空房对象、门、主入口或默认恢复装置错误");
            Require(draft.World == null && draft.WorkPath == null && draft.Dirty && draft.Catalog.Count == 1 && draft.ActiveId != "qa-expand", "新建未重置旧工作区");
        }

        static void BlankRoundTrip()
        {
            var draft = FixtureDraft();
            draft.NewBlankRoom();
            string snapshot = draft.Snapshot();
            var restored = FixtureDraft();
            restored.Recover(snapshot);
            Require(restored.Dirty && restored.Snapshot() == snapshot, "空白草稿往返丢失状态");
        }

        static void BlankSave()
        {
            var draft = FixtureDraft();
            draft.NewBlankRoom();
            string before = draft.Snapshot();
            string path = Path.Combine(Application.temporaryCachePath, Guid.NewGuid().ToString("N") + ".room.json");
            try
            {
                draft.Save(path, false);
                throw new InvalidOperationException("正式保存接受了空白草稿");
            }
            catch (InvalidDataException ex) when (ex.Message.Contains("出生点"))
            {
                Require(!File.Exists(path) && before == draft.Snapshot() && draft.WorkPath == null, "保存拒绝后文件或草稿被修改");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }

        static void Repave()
        {
            var draft = FixtureDraft();
            draft.NewBlankRoom();
            var cell = new Cell(7, 7);
            Paint(draft, cell, RoomTool.Floor);
            Require(Terrain(draft, cell) == TerrainCategories.Floor, "空格铺地失败");
            Paint(draft, cell, RoomTool.Erase);
            Require(Terrain(draft, cell) == TerrainCategories.Void, "地板未擦除");
            Paint(draft, cell, RoomTool.Wall);
            Require(Terrain(draft, cell) == TerrainCategories.Wall && Terrain(draft, cell + Cell.Right) == TerrainCategories.Void, "重铺墙影响错误格");
            draft.Undo();
            Require(Terrain(draft, cell) == TerrainCategories.Void, "撤销重铺未恢复空格");
            draft.Redo();
            Require(Terrain(draft, cell) == TerrainCategories.Wall, "重做未恢复墙");
        }

        static void FormalRoomReopen()
        {
            string path = WarehousePath();
            var draft = FixtureDraft();
            draft.NewBlankRoom();
            draft.Open(path, false);
            Require(!draft.Dirty && draft.WorkPath == Path.GetFullPath(path) && draft.World == null, "正式房间文件关联或脏状态错误");
            Require(draft.ActiveId == "warehouse" && draft.Room.Width == 9 && draft.Room.Height == 7, "正式房间尺寸错误");
            Require(draft.Find("spawn") != null && draft.Find("box-1") != null && draft.Find("target-1") != null && draft.Model.Doors.Any(d => d.Id == "entry"), "正式房间关键对象缺失");
        }

        static void ProtectedPreview()
        {
            var draft = FixtureDraft();
            string before = draft.Snapshot();
            foreach (var cell in new[] { new Cell(2, 2), new Cell(4, 2), new Cell(6, 2), new Cell(1, 2) })
            {
                var error = draft.TerrainPlacementError(cell, RoomTool.Wall);
                Require(error != null && error.Contains("占用") && error.Contains(cell.ToString()), "保护格预检错误 " + cell);
                Require(draft.TerrainPlacementError(cell, RoomTool.Floor) == null, "保护格误拒地板 " + cell);
            }
            Require(draft.TerrainPlacementError(new Cell(3, 3), RoomTool.Wall) == null, "普通格误拒墙");
            Require(before == draft.Snapshot(), "保护格预检改变草稿");
        }

        static void BuildAndReopen()
        {
            var draft = FixtureDraft();
            draft.NewBlankRoom();
            var states = new List<string> { RoomCodec.Encode(draft.Model) };
            void Snap() => states.Add(RoomCodec.Encode(draft.Model));
            var corridor = Enumerable.Range(3, 5).Select(x => new Cell(x, 7)).ToArray();
            Require(draft.Brush(corridor, RoomTool.Floor) == null, "铺设走廊失败"); Snap();
            Require(draft.Brush(corridor.SelectMany(c => new[] { c + Cell.Up, c + Cell.Down }), RoomTool.Wall) == null, "封墙失败"); Snap();
            draft.SetRoomMarker(corridor[0], "entry"); Snap();
            draft.SetRoomMarker(corridor[2], "spawn"); Snap();
            draft.Place(true, corridor[2]); Snap();
            draft.Place(false, corridor[3]); Snap();
            draft.SetRoomMarker(corridor[4], "exit"); Snap();
            draft.PlaceRecoveryDevice(new RecoveryDevice("recovery-qa", RecoveryDeviceKind.Forklift, new Cell(-1, 7), 1, "warehouse-lift", 3.7f), replaceExisting: true); Snap();
            var entry = draft.Model.Doors.Single(d => d.Role == "entry");
            var exit = draft.Model.Doors.Single(d => d.Role == "exit");
            Require(draft.Room.ObjectsOf(ObjectCategories.Spawn).Single().Cell == corridor[1] && entry.Cell == corridor[0] && exit.Cell == corridor[4], "标记坐标错误");
            Require(entry.Outward == Cell.Left && exit.Outward == Cell.Right && draft.Model.PrimaryEntranceId == entry.Id, "门朝向或主入口错误");
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/WS05A-" + draft.ActiveId + ".room.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            draft.Save(path, false);
            string contents = File.ReadAllText(path);
            Require(contents == states[states.Count - 1] && !draft.Dirty && draft.WorkPath == path, "正式文件或保存状态错误");
            for (int i = states.Count - 2; i >= 0; i--)
            {
                draft.Undo();
                Require(draft.Dirty && RoomCodec.Encode(draft.Model) == states[i], "撤销步骤错误 " + i);
            }
            for (int i = 1; i < states.Count; i++)
            {
                draft.Redo();
                Require(RoomCodec.Encode(draft.Model) == states[i], "重做步骤错误 " + i);
            }
            Require(!draft.Dirty && File.ReadAllText(path) == contents, "重做后保存基线或磁盘改变");
            draft.Open(path, false);
            Require(!draft.Dirty && draft.WorkPath == path && RoomCodec.Encode(draft.Model) == contents, "正式文件重读不一致");
            try { draft.Undo(); throw new InvalidOperationException("重开继承旧编辑历史"); }
            catch (InvalidDataException) { }
            Debug.Log("WS05A ROOM " + path);
        }

        static void AutoSlot()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/solo-" + Guid.NewGuid().ToString("N")));
            var panel = new LayoutPanel("terrain", folder);
            Paint(panel.Draft, new Cell(-1, -1), RoomTool.Wall);
            string path = Path.Combine(folder, "terrain-ws02.json");
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!File.Exists(path) && timer.ElapsedMilliseconds < 5000)
            {
                panel.Tick();
                System.Threading.Thread.Sleep(25);
            }
            Require(File.Exists(path), "自动草稿未落盘");
            var restored = new LayoutPanel("terrain", folder).Draft;
            Require(restored.Origin == new Cell(-1, -1) && RoomCodec.Encode(restored.Model) == RoomCodec.Encode(panel.Draft.Model), "跨会话草稿未恢复原点或布局");
            try { restored.Undo(); throw new InvalidOperationException("恢复的草稿不应保留撤销历史"); }
            catch (InvalidDataException) { }
            Debug.Log("WS05A SLOT " + path);
        }

        static void Export()
        {
            var draft = FixtureDraft();
            Paint(draft, new Cell(-1, 3), RoomTool.Wall);
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/solo-" + Guid.NewGuid().ToString("N") + ".room.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            draft.Save(path, true);
            string text = File.ReadAllText(path);
            Require(!text.Contains("editorOrigin") && text == RoomCodec.Encode(draft.Model) && draft.Origin == new Cell(-1, 0) && draft.WorkPath == null, "导出泄漏临时原点或改变布局");
            var reopened = FixtureDraft();
            reopened.Open(path, false);
            Require(reopened.Origin == new Cell(0, 0) && RoomCodec.Encode(reopened.Model) == text && !reopened.Dirty, "独立正式房间重开后原点或局部布局错误");
            Debug.Log("WS05A EXPORT " + path);
        }

        static void Trial()
        {
            var draft = FixtureDraft();
            Paint(draft, new Cell(-1, 3), RoomTool.Wall);
            var run = PuzzleRun.FromDraft(draft);
            var spawn = draft.Room.ObjectsOf(ObjectCategories.Spawn).Single().Cell;
            Require(run.Origin == Vector2Int.zero && run.PlayerLocal == spawn, "合法单房试玩坐标错误");
            var wall = new Cell(4, 0);
            Require(Terrain(draft, wall) == TerrainCategories.Wall, "样例南墙缺失");
            Paint(draft, wall, RoomTool.Erase);
            Require(RoomValidator.ValidateDraft(draft.Model, ValidationStage.Publish).Any(i => i.RuleId == "boundary.open" && i.Location == (wall + Cell.Up).ToString()), "测试未形成地板开口");
            string before = draft.Snapshot();
            try { PuzzleRun.FromDraft(draft); throw new InvalidOperationException("试玩接受了缺墙房间"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("boundary.open")) { }
            Require(before == draft.Snapshot(), "拒绝试玩改变了草稿");
            draft.Undo();
            Require(PuzzleRun.FromDraft(draft).PlayerLocal == spawn, "修复后试玩出生位置错误");
            draft.NewBlankRoom();
            try { PuzzleRun.FromDraft(draft); throw new InvalidOperationException("试玩接受了无出生点空房"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("spawn.missing")) { }
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
            Test("east", () => Expansion(new Cell(9, 1), new Cell(0, 0), new Cell(1, 0), BrushTool.Floor, TerrainCategories.Floor));
            Test("west", () => Expansion(new Cell(-1, 1), new Cell(1, 0), new Cell(1, 0), BrushTool.Wall, TerrainCategories.Wall));
            Test("north", () => Expansion(new Cell(1, 5), new Cell(0, 0), new Cell(0, 1), BrushTool.Wall, TerrainCategories.Wall));
            Test("south", () => Expansion(new Cell(1, -1), new Cell(0, 1), new Cell(0, 1), BrushTool.Floor, TerrainCategories.Floor));
            Test("whole-stroke-rejected", WholeStroke);
            Test("door-cells-protected", DoorsProtected);
            Test("erase-keeps-objects", EraseKeepsObjects);
            Test("world-view-stroke-rejected", WorldViewRejected);
            Test("world-view-preview", WorldPreview);
            Test("standalone-expand-overlap", StandaloneOverlap);
            Test("history-draft", History);
            Test("standalone-origin-history", StandaloneOrigin);
            Test("blank-state", BlankState);
            Test("blank-draft-roundtrip", BlankRoundTrip);
            Test("blank-formal-save-refused", BlankSave);
            Test("blank-repave", Repave);
            Test("formal-room-reopen", FormalRoomReopen);
            Test("blank-build-save-history-reopen", BuildAndReopen);
            Test("preview-protected-cells", ProtectedPreview);
            Test("solo-auto-slot-origin", AutoSlot);
            Test("solo-export-reopen-origin", Export);
            Test("solo-trial-structure", Trial);
            Debug.Log($"WS05A RESULT {total - failed}/{total}");
            Require(failed == 0, $"WS05A {failed} failed");
        }
    }
}
