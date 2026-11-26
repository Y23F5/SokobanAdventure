/**
 * @file GridPlayVerification.cs
 * @brief Run 复核扩建后门前空格与普通空格的移动规则，打开运行编辑器进入 Play Mode。Tick 按帧驱动流程与退出码。
 * OrdinaryVoid：向普通非门格外的空格迈步被拒，存档快照逐字段一致。
 * SparseExpansion：单独编辑扩建后出口前方成为包围盒内部空格，门几何校验仍通过。
 * CrossDoor：正式世界里仓库解开后经出口向空格迈步传送到工厂入口，并经同一连接返回。
 * Flow：检查实际网格、虚影、试玩和正式局；Hover 通过编辑宿主绘制确定性射线命中的格子。
 */
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Sokoban.Core;
using Object = UnityEngine.Object;

namespace Sokoban.Editor
{
    public static class GridPlayVerification
    {
        const string Key = "Sokoban.QaGrid.Active";
        static IEnumerator flow;
        static int frame = -1;

        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
            Debug.Log("GRIDQA PASS " + message);
        }

        static string WarehousePath() => Path.Combine(Application.streamingAssetsPath, "World/warehouse.room.json");

        static void OrdinaryVoid()
        {
            var run = PuzzleRun.FromDraft(new RoomDraft(RoomFile.Load(WarehousePath())));
            var above = run.PlayerLocal + Cell.Up;
            Check(run.Room.Room.TerrainAt(above).CategoryId == TerrainCategories.Floor, "出生点上方为普通地板");
            run.Room.Room.SetTerrain(above, TerrainCategories.Void);
            string before = SaveCodec.ToJson(run.Snapshot());
            Check(!run.Room.Doors.Any(d => d.Cell == above) && !run.Step(Vector2Int.up, out var pushed) && pushed == null && before == SaveCodec.ToJson(run.Snapshot()), "普通非门格向内部 Void 迈步被拒，运行快照逐字段一致");
        }

        static void SparseExpansion()
        {
            var draft = new RoomDraft(RoomFile.Load(WarehousePath()));
            Check(draft.Brush(new[] { new Cell(10, 0) }, RoomTool.Wall) == null, "扩建东侧包围盒");
            var outside = new Cell(9, 3);
            Check(draft.Room.Width == 11 && draft.Room.Inside(outside) && draft.Room.TerrainAt(outside).CategoryId == TerrainCategories.Void, "出口前方为包围盒内部 Void");
            Check(!RoomValidator.ValidateDraft(draft.Model, ValidationStage.Publish).Any(i => i.RuleId == "door.geometry"), "扩建后门几何校验仍通过");
        }

        static void CrossDoor()
        {
            var run = PuzzleRun.Campaign();
            Check(run.ActiveId == "warehouse" && !run.Solved, "正式局从未解开的仓库开始");
            WS06WorldSolutionVerification.SolveInRoom(run);
            Check(run.Solved, "实走推箱解开出口");
            var exit = run.Room.Doors.Single(d => d.Role == "exit");
            WS06WorldSolutionVerification.WalkTo(run, exit.Cell);
            var outward = new Vector2Int(exit.Outward.X, exit.Outward.Y);
            Check(run.Step(outward, out _) && run.ActiveId == "factory" && run.PlayerLocal == run.Room.Doors.Single(d => d.Id == "entry").Cell, "向房外 Void 迈步，传送至 factory 入口 " + run.PlayerLocal);
            Check(run.Step(Vector2Int.left, out _) && run.ActiveId == "warehouse" && run.PlayerLocal == exit.Cell, "经同一连接反向返回");
        }

        public static void Run()
        {
            OrdinaryVoid();
            SparseExpansion();
            CrossDoor();
            EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Hook() { EditorApplication.update += Tick; }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !Application.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try
            {
                if (flow == null) flow = Flow();
                if (flow.MoveNext()) return;
                SessionState.EraseBool(Key);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                SessionState.EraseBool(Key);
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        static int GridLines() => Object.FindObjectsOfType<LineRenderer>().Count(l => l.enabled && l.name == "格线" && Mathf.Approximately(l.startWidth, .014f));

        static void Hover(LayoutHost host, RoomTool tool, Vector2Int cell, bool valid)
        {
            var ray = new Ray(new Vector3(cell.x + .42f, 5, cell.y - .43f), Vector3.down);
            if (!RoomPreview.CellFromRay(ray, out var hit) || hit != cell) throw new InvalidOperationException("射线吸附错误");
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            host.Panel.Draft.Tool = tool;
            typeof(LayoutHost).GetField("hoveredCell", flags).SetValue(host, hit);
            typeof(LayoutHost).GetField("hovering", flags).SetValue(host, true);
            typeof(LayoutHost).GetMethod("LateUpdate", flags).Invoke(host, null);
            var model = GameObject.Find("对象放置预览");
            if (!model) throw new InvalidOperationException("缺少虚影 " + tool);
            var tint = model.GetComponentInChildren<Renderer>().sharedMaterial.GetColor("_BaseColor");
            var color = valid ? Color.green : Color.red;
            color.a = .43f;
            Check(model.transform.position == new Vector3(cell.x, .025f, cell.y) && tint == color && (host.HoverFeedback == null) == valid, tool + " 虚影位置 " + model.transform.position + " 颜色 " + tint + " 反馈 " + host.HoverFeedback);
        }

        static IEnumerator Flow()
        {
            yield return null; yield return null;
            var host = Object.FindObjectOfType<LayoutHost>();
            var panel = host.Panel;
            var draft = panel.Draft;
            draft.NewBlankRoom(); yield return null; yield return null;
            Check(host.GridVisible && GridLines() == 32, "15×15 空房默认 32 条网格线");
            host.Interface.Act("网格"); yield return null; yield return null;
            Check(!host.GridVisible && GridLines() == 0, "网格开关关闭实际线条");
            host.Interface.Act("网格"); yield return null; yield return null;
            Check(GridLines() == 32, "重新开启网格恢复 32 条线");
            Hover(host, RoomTool.Floor, new Vector2Int(6, 6), true);
            Hover(host, RoomTool.Box, new Vector2Int(6, 6), false);
            draft.Open(WarehousePath(), false);
            Hover(host, RoomTool.Box, new Vector2Int(2, 4), true);
            Hover(host, RoomTool.Box, new Vector2Int(4, 2), false);
            Hover(host, RoomTool.Wall, new Vector2Int(1, 3), false);
            draft.Place(true, new Cell(2, 4)); draft.Tool = RoomTool.Select;
            int roomLines = draft.Room.Width + 1 + draft.Room.Height + 1;
            var snapshot = draft.Snapshot(); var camera = Camera.main.transform.position;
            host.Interface.Act("试玩"); yield return null; yield return null;
            var game = Object.FindObjectOfType<PuzzleHost>();
            Check(game && game.IsTrial && GridLines() == 0 && !host.Interface.gameObject.activeInHierarchy, "试玩隐藏编辑网格和界面");
            Check(game.Run.Step(Vector2Int.up, out _), "试玩副本实际移动");
            game.Leave(); yield return null; yield return null;
            Check(host.Interface.gameObject.activeInHierarchy && GridLines() == roomLines && snapshot == draft.Snapshot() && Vector3.Distance(camera, Camera.main.transform.position) < .001f, "退出试玩恢复编辑地图、相机、选择、脏状态及历史");
            SceneManager.LoadScene("MainMenu"); yield return null; yield return null;
            Object.FindObjectOfType<MainMenuNavigation>().NewCampaign();
            for (int i = 0; i < 600 && !Object.FindObjectOfType<PuzzleHost>(); i++) yield return null;
            yield return null; yield return null;
            game = Object.FindObjectOfType<PuzzleHost>();
            Check(game && game.Run != null && !game.IsTrial && GridLines() == 0 && !Object.FindObjectOfType<LayoutHost>(), "主菜单新建正式局，无编辑网格");
        }
    }
}
