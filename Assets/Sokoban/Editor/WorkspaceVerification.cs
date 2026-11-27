/**
 * @file WorkspaceVerification.cs
 * @brief Run 通过正式样例验证世界坐标、编辑历史、交换与恢复槽并打包；Check 将断言结果写到独立证据目录。
 * Check：把断言结果追加到证据目录并在失败时抛出；Throws：断言动作必须抛出异常；FreeCell：取第一个可放箱子的格。
 * VerifyRecovery：经 LayoutPanel 恢复槽验证快照字符串、脏标记、视角与损坏回退，快照不含选择、工具与撤销历史。
 * VerifyFiles：验证保存、导出、导入与失败写入的工作关联和脏标记，比较 RoomCodec 文本。
 * VerifyUnplacedRoomSnapshot()：从正式三房移出工厂，经草稿恢复后重新放回，核对连接和前置关系。
 * Run：验证世界原点平移、重叠拒绝、边界、快照恢复、传送导出与整笔画刷语义，最后构建 Windows 包。
 */
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Sokoban.Core;

namespace Sokoban.Editor
{
    public static class WorkspaceVerification
    {
        static string evidence;

        static void Check(bool condition, string message)
        {
            File.AppendAllText(Path.Combine(evidence, "checks.txt"), (condition ? "PASS " : "FAIL ") + message + Environment.NewLine);
            if (!condition) throw new InvalidOperationException(message);
        }

        static bool Throws(Action action)
        {
            try { action(); return false; }
            catch (Exception) { return true; }
        }

        static Cell FreeCell(RoomDraft draft, string ignore)
        {
            for (int y = 0; y < draft.Room.Height; y++)
                for (int x = 0; x < draft.Room.Width; x++)
                    if (draft.PlacementError(true, new Cell(x, y), ignore) == null && draft.Find(ignore)?.Cell != new Cell(x, y)) return new Cell(x, y);
            throw new InvalidOperationException("房间内没有可放置箱子的格");
        }

        static void VerifyRecovery(string source)
        {
            var panel = new LayoutPanel("test", evidence);
            panel.Draft.Tool = RoomTool.Box; panel.Draft.Place(true, FreeCell(panel.Draft, null));
            var selected = panel.Draft.Selected; panel.SetView(new Vector3(4, 0, 1), Quaternion.Euler(42, 15, 0), 8, true); panel.Tick();
            System.Threading.Thread.Sleep(1100); panel.Tick(); string path = Path.Combine(evidence, "test-ws02.json");
            var restored = new LayoutPanel("test", evidence);
            Check(restored.Draft.Find(selected) != null && restored.Draft.Dirty && restored.Zoom == 8 && restored.Pivot == new Vector3(4, 0, 1) && restored.Draft.Snapshot() == panel.Draft.Snapshot(), "空闲一秒实盘恢复房间快照、箱子、视角、脏标记");
            Check(restored.Draft.Selected == null && restored.Draft.Tool == RoomTool.Select, "快照不含选择与工具，恢复后回到选择工具");
            Check(Throws(restored.Draft.Undo), "快照不含撤销历史，恢复后撤销被拒绝");
            DateTime first = File.GetLastWriteTimeUtc(path); var clock = System.Diagnostics.Stopwatch.StartNew(); int step = 0;
            while (clock.Elapsed.TotalSeconds < 10.6) { panel.SetView(new Vector3(++step, 0, 1), Quaternion.identity, 7, true); panel.Tick(); System.Threading.Thread.Sleep(140); }
            Check(File.GetLastWriteTimeUtc(path) > first, "持续编辑超过十秒，期间更新磁盘快照");
            string good = File.ReadAllText(path); var damaged = Path.Combine(evidence, "damaged-ws02.json"); File.WriteAllText(damaged, "{");
            var fallback = new LayoutPanel("damaged", evidence); var fixedRoom = RoomCodec.Decode(source).Room;
            Check(fallback.Status.Contains("恢复失败") && fallback.Draft.Room.Width == fixedRoom.Width && fallback.Draft.Room.Height == fixedRoom.Height && fallback.Draft.Room.Objects.Count == fixedRoom.Objects.Count && File.ReadAllText(path) == good, "损坏快照回到固定房间并报告原因，其他文件不变");
        }

        static void VerifyFiles(string source)
        {
            var draft = new RoomDraft(RoomCodec.Decode(source)); string first = Path.Combine(evidence, "A.room.json"), second = Path.Combine(evidence, "B.room.json"), third = Path.Combine(evidence, "C.room.json");
            draft.Save(first, false); draft.Open(first, false); draft.Place(true, FreeCell(draft, null)); draft.Save(second, true);
            Check(draft.Dirty && draft.WorkPath == Path.GetFullPath(first) && File.ReadAllText(first) != RoomCodec.Encode(draft.Model), "导出 B 不改 A 和脏标记");
            var before = draft.Snapshot(); bool unwritable = Throws(() => draft.Save(evidence, false));
            bool unreadable = Throws(() => draft.Open(Path.Combine(evidence, "damaged-ws02.json"), false));
            Check(unwritable && unreadable && before == draft.Snapshot(), "不可写目标及损坏候选均被拒绝，并保留原草稿、选择、历史与工作关联");
            draft.Save(first, false); Check(!draft.Dirty && File.ReadAllText(first) == File.ReadAllText(second), "保存回 A 并清除脏标记");
            draft.Open(second, true); Check(draft.WorkPath == null, "导入解除关联"); draft.Save(third, false);
            Check(draft.WorkPath == Path.GetFullPath(third) && File.ReadAllText(third) == File.ReadAllText(second), "首次保存建立 C，交换内容只有房间设计");
        }

        public static void VerifyUnplacedRoomSnapshot()
        {
            evidence = "Logs/workspace-prerequisites";
            Directory.CreateDirectory(evidence);
            File.WriteAllText(Path.Combine(evidence, "checks.txt"), "");
            string source = Path.GetFullPath("Assets/StreamingAssets/World/main.world.json");
            var seed = RoomCodec.Decode(File.ReadAllText(Path.Combine(Path.GetDirectoryName(source), "warehouse.room.json")));
            var draft = new RoomDraft(seed);
            draft.Open(source, false);
            draft.UseRoom("factory");
            var factory = draft.Placement;
            string original = WorldCodec.Encode(draft.World);

            draft.RemoveRoom();
            Check(WorldLayout.Issues(draft.World, draft.Catalog).Any(), "工厂暂未放置时正式校验报告无效引用");
            string snapshot = draft.Snapshot();
            var restored = new RoomDraft(seed);
            restored.Recover(snapshot);
            Check(restored.ActiveId == "factory" && restored.Placement == null && restored.Snapshot() == snapshot,
                "草稿重开仍保留未放置的工厂、森林前置条件与连接");
            restored.PlaceRoom(factory.Origin, false);
            var ordered = WorldLayout.WithRooms(restored.World, restored.World.Rooms.OrderBy(row =>
                Array.IndexOf(new[] { "warehouse", "factory", "forest" }, row.LevelId)).ToArray());
            Check(WorldCodec.Encode(ordered) == original && !WorldLayout.Issues(restored.World, restored.Catalog).Any(),
                "工厂放回原位后恢复全部原始引用及正式校验");
            restored.Undo();
            Check(restored.Snapshot() == snapshot, "撤销重新放置后仍可保存未放置状态");
            draft.Undo();
            Check(WorldCodec.Encode(draft.World) == original, "原会话撤销移出恢复完整世界");
        }

        public static void Run()
        {
            evidence = "Docs/验收证据/WS-02/" + DateTime.Now.ToString("yyyyMMdd-HHmmss"); Directory.CreateDirectory(evidence);
            string source = Resources.Load<TextAsset>("DefaultRoom").text;
            var draft = new RoomDraft(RoomCodec.Decode(source)); draft.Open("Docs/关卡样例/main.world.json", false); draft.UseRoom("factory");
            var original = draft.World; string originalText = WorldCodec.Encode(original); string roomText = RoomCodec.Encode(draft.Model);
            Check(!WorldLayout.Issues(draft.World, draft.Catalog).Any(), "正式样例初始两条直连有效");
            draft.PlaceRoom(new Cell(40, 10), true);
            Check(WorldLayout.Issues(draft.World, draft.Catalog).Count() == 2 && draft.World.Connections.SequenceEqual(original.Connections) && RoomCodec.Encode(draft.Model) == roomText, "MAP02-003 工厂平移导致两条直连失效，端点与局部布局不变");
            draft.Undo(); Check(WorldCodec.Encode(draft.World) == originalText, "撤销恢复原点和直连");
            var valid = Path.Combine(evidence, "valid.world.json"); var written = WorldLayout.Write(valid, original, draft.Catalog);
            var overlapped = WorldLayout.WithRooms(written, written.Rooms.Select((r, i) => i == 1 ? new WorldRoomRef(r.LevelId, r.File, written.Rooms[0].Origin, r.ForwardRequiresSolved) : r).ToList());
            string invalidFile = Path.Combine(evidence, "invalid-overlap.world.json"); File.WriteAllText(invalidFile, WorldCodec.Encode(overlapped));
            var prior = draft.Snapshot(); string refusal = null;
            try { draft.Open(invalidFile, false); } catch (Exception ex) { refusal = ex.Message; }
            Check(refusal != null && refusal.Contains("重叠") && prior == draft.Snapshot(), "MAP02-002 从磁盘打开重叠世界被拒绝并保留当前工作区");
            var size = new Cell(draft.Room.Width, draft.Room.Height); var edge = new Cell(64 - size.X, 32 - size.Y);
            Check(WorldLayout.PlacementIssue(draft.World, draft.Catalog, "factory", edge, true) == null && WorldLayout.PlacementIssue(draft.World, draft.Catalog, "factory", edge + Cell.Right, true) != null && WorldLayout.PlacementIssue(draft.World, draft.Catalog, "factory", new Cell(8, 0), true) != null && WorldLayout.PlacementIssue(draft.World, draft.Catalog, "factory", edge, false) != null, "MAP02-002 贴边合法、越界一格、重叠一格、重复房间拒绝");
            draft.RemoveRoom(); draft.PlaceRoom(edge, false);
            var box = draft.Room.ObjectsOf(ObjectCategories.Box).First(); draft.Select(box.Cell); string id = draft.Selected;
            Check(id == box.Id, "按格选择取得箱子对象");
            var free = FreeCell(draft, id);
            draft.Move(free); draft.Tool = RoomTool.Move;
            var restored = new RoomDraft(RoomCodec.Decode(source)); restored.Recover(draft.Snapshot());
            Check(restored.ActiveId == "factory" && restored.Origin == edge && restored.Find(id).Cell == free && restored.Dirty && restored.WorkPath == draft.WorkPath, "跨会话快照恢复活动房间、箱子位置、原点、工作路径和未保存状态");
            Check(restored.Selected == null && restored.Tool == RoomTool.Select && Throws(restored.Undo), "快照不含选择、工具与撤销历史，恢复后回到选择工具且撤销被拒绝");
            draft.Undo(); Check(draft.Find(id).Cell == box.Cell, "原会话撤销恢复箱子原位");
            draft.Redo(); Check(draft.Find(id).Cell == free, "原会话重做回到移动后的格");
            foreach (var link in restored.World.Connections.ToList()) restored.SetConnectionMode(link.Id, WorldCodec.Teleport);
            string worldFile = Path.Combine(evidence, "exchange.world.json"); restored.Save(worldFile, true);
            Check(restored.Dirty && restored.WorkPath == draft.WorkPath, "远端房间显式改传送后导出，保留原关联与脏标记");
            var reader = new RoomDraft(RoomCodec.Decode(source)); reader.Open(worldFile, true); reader.UseRoom("factory");
            Check(reader.Origin == edge && reader.Find(id).Cell == free && reader.WorkPath == null && reader.World.Connections.All(c => c.Mode == WorldCodec.Teleport), "另端解码相对引用、局部坐标与传送连接");
            reader.NewDefaultRoom(); reader.Place(true, FreeCell(reader, null));
            var protectedDoor = reader.Model.Doors.First();
            Cell? a = null;
            for (int y = 0; y < reader.Room.Height && !a.HasValue; y++)
                for (int x = 0; x < reader.Room.Width && !a.HasValue; x++)
                {
                    var c = new Cell(x, y); var d = c + Cell.Right;
                    bool plain = reader.Room.TerrainAt(c).CategoryId == TerrainCategories.Floor && reader.Room.TerrainAt(d).CategoryId == TerrainCategories.Floor && reader.Room.ObjectAt(c) == null && reader.Room.ObjectAt(d) == null;
                    bool nearDoor = reader.Model.Doors.Any(door => door.Cell - door.Outward == c || door.Cell - door.Outward == d);
                    if (plain && !nearDoor) a = c;
                }
            Check(a.HasValue, "房间内存在两块相邻的空地板格用于画刷测试");
            var strokeA = a.Value; var strokeB = a.Value + Cell.Right;
            Check(reader.Brush(new[] { strokeA, strokeB }, RoomTool.Wall) == null && reader.Room.TerrainAt(strokeA).CategoryId != TerrainCategories.Floor && reader.Room.TerrainAt(strokeB).CategoryId != TerrainCategories.Floor, "一笔画墙两格全部生效");
            reader.Undo(); Check(reader.Room.TerrainAt(strokeA).CategoryId == TerrainCategories.Floor && reader.Room.TerrainAt(strokeB).CategoryId == TerrainCategories.Floor, "一次撤销恢复整笔");
            string stable = RoomCodec.Encode(reader.Model);
            Check(reader.Brush(new[] { strokeA, protectedDoor.Cell }, RoomTool.Wall) != null && RoomCodec.Encode(reader.Model) == stable, "笔划含门格时整笔拒绝，合法格也不提交");
            Check(reader.Brush(new[] { strokeA, protectedDoor.Cell }, RoomTool.Floor) != null && reader.Brush(new[] { strokeA, protectedDoor.Cell }, RoomTool.Erase) != null && RoomCodec.Encode(reader.Model) == stable, "门格对地板与擦除工具同样受保护，整笔拒绝");
            var placedBox = reader.Room.ObjectsOf(ObjectCategories.Box).First();
            Check(reader.Brush(new[] { strokeA, placedBox.Cell }, RoomTool.Erase) != null && RoomCodec.Encode(reader.Model) == stable, "擦除遇对象整笔拒绝，不连带删除对象");
            VerifyRecovery(source); VerifyFiles(source);
            var result = BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/MainMenu.unity", "Assets/Scenes/Game.unity", "Assets/Scenes/RuntimeEditor.unity" }, "Builds/WS-02/Sokoban.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
            Check(result.summary.result == BuildResult.Succeeded && result.summary.totalErrors == 0, "Windows x64 构建");
        }
    }
}
