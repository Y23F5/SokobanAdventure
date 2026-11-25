/**
 * @file AppearanceStateVerification.cs
 * @brief Prepare verifies typed appearances, saves and exchanges a mixed room, and publishes the forest through the Editor configuration command.
 * Run drives the real RuntimeEditor cards, trial and recovery; Tick advances the checks and exits the batch editor.
 */
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Sokoban.Editor
{
    public static class AppearanceStateVerification
    {
        const string Key = "Sokoban.AppearanceStateVerify";
        static string Folder => Environment.GetEnvironmentVariable("SOKOBAN_APPEARANCE_VERIFY");
        static string Forest => Path.Combine(Folder, "forest.room.json");
        static string Mixed => Path.Combine(Folder, "mixed.room.json");
        static IEnumerator steps;
        static int frame = -1;

        static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            Debug.Log("APPEARANCE PASS " + label);
        }

        public static void Prepare()
        {
            Directory.CreateDirectory(Folder);
            var window = RoomWindow.Open();
            var draft = window.Panel.Draft;
            draft.Open(Path.Combine(Application.streamingAssetsPath, "World", "forest.room.json"), true);
            foreach (var box in draft.Room["boxes"].ToArray())
            {
                var cell = RoomFile.At(box);
                draft.Select(cell);
                var editor = new ObjectStateEditor(draft);
                editor.UseForestPreset();
                editor.Apply(draft);
            }
            draft.ExportRoom(Forest);
            var read = RoomAssets.ReadRoom(Forest);
            Check((int)read["formatVersion"] == 3 && read["boxes"].All(box => JToken.DeepEquals(box["stateProfile"], ObjectStateProfile.ForestProfile())),
                "Unity 房间工具配置两个正式森林箱，通过外部编辑器的同一属性命令发布 v3");

            draft.NewDefaultRoom();
            var theme = (string)draft.Room["themeId"];
            draft.ChooseAppearance("FactoryWall", RoomTool.Wall);
            draft.Brush(new[] { new Vector2Int(1, 6) }, RoomTool.Wall);
            draft.ChooseAppearance("ForestWall", RoomTool.Wall);
            draft.Brush(new[] { new Vector2Int(2, 6) }, RoomTool.Wall);
            draft.ChooseAppearance("forestTile2", RoomTool.Floor);
            draft.Brush(new[] { new Vector2Int(2, 4) }, RoomTool.Floor);
            draft.ChooseAppearance("factoryTile1", RoomTool.Floor);
            draft.Brush(new[] { new Vector2Int(3, 4) }, RoomTool.Floor);
            draft.ChooseAppearance("Logs", RoomTool.Box);
            draft.Click(new Vector2Int(2,4));
            string log = draft.Selected;
            draft.ChooseAppearance("ForestTarget", RoomTool.Target);
            draft.Click(new Vector2Int(3,4));
            draft.ChooseAppearance("ForestFence", RoomTool.Decoration);
            draft.Click(new Vector2Int(4,4));
            var snapshot = draft.Snapshot();
            draft.Recover(JObject.Parse(snapshot.ToString()));
            Check(draft.BrushBuiltin == "ForestFence" && draft.Tool == RoomTool.Decoration, "草稿重开还原内置笔刷及分类");
            var original = draft.Room.DeepClone();
            draft.SetTheme("forest");
            Check((string)draft.Room["themeId"] == "forest" && (string)draft.Find(log)["builtinModel"] == "Logs" &&
                (string)draft.Room["boxes"].First["builtinModel"] == AppearanceLibrary.Resolve(RoomTool.Box, theme, Vector2Int.zero).Id &&
                draft.Room["terrainObjects"].Any(entry => (string)entry["builtinModel"] == "FactoryWall") &&
                draft.Room["terrainObjects"].Any(entry => (string)entry["builtinModel"] == "forestTile2"), "更换主题保留新旧模型、各自类型和格子");
            var themed = draft.Room.DeepClone();
            draft.Undo(); Check(JToken.DeepEquals(original,draft.Room), "换主题一次撤销");
            draft.Redo(); Check(JToken.DeepEquals(themed,draft.Room), "换主题一次重做");
            draft.ExportRoom(Mixed);
            draft.Open(Mixed, false);
            Check((string)draft.Find(log)["builtinModel"] == "Logs" && (int)draft.Room["formatVersion"] == 3, "混搭单房保存导出后重开");

            string modelPath = Environment.GetEnvironmentVariable("SOKOBAN_APPEARANCE_GLB");
            draft.Tool = RoomTool.Box;
            string asset = draft.ImportAsset(modelPath, "model");
            draft.BeginWorld();
            draft.Attach(Forest);
            draft.UseRoom("forest");
            Check(draft.CategoryAssets(RoomTool.Box).Contains(asset), "同一世界的森林房可以直接复用另一房导入的箱体");
            var free = Enumerable.Range(0, RoomFile.Size(draft.Room).x * RoomFile.Size(draft.Room).y)
                .Select(i => new Vector2Int(i % RoomFile.Size(draft.Room).x, i / RoomFile.Size(draft.Room).x))
                .First(cell => draft.PlacementError(true, cell) == null);
            draft.SelectAsset(asset, RoomTool.Box);
            draft.Click(free);
            Check(draft.Room["assets"]?[asset] != null && (string)draft.Find(draft.Selected)["visual"]["model"] == asset,
                "跨房使用的 GLB 和类别记录随目标房间一起保存");
            string standalone = Path.Combine(Folder, "reuse.room.json");
            draft.ExportRoom(standalone);
            Check(RoomAssets.ReadRoom(standalone)["assets"]?[asset] != null, "独立导出不依赖借用来源房间");

            draft.NewDefaultRoom();
            string decorModel = draft.ImportAsset(modelPath, "model");
            draft.SelectAsset(decorModel);
            var decorationCell = new Vector2Int(4,4);
            draft.Click(decorationCell);
            string decoration = draft.Selected;
            draft.SetStateProfile(new JObject { ["initial"] = "idle", ["states"] = new JObject
                { ["idle"] = new JObject(), ["changed"] = new JObject { ["builtinModel"] = "ForestFence" } }, ["transitions"] = new JArray() });
            var graph = ObjectBehavior.Template();
            var node = (JObject)graph["nodes"].Last;
            node["kind"] = "setState";
            node["state"] = "changed";
            node.Remove("visible");
            draft.SetBehavior(decorationCell, graph);
            draft.DefineObject("可切换的装饰");
            draft.SelectDefinition(((JObject)draft.Room["objectDefinitions"]).Properties().Single().Name);
            draft.Click(new Vector2Int(5,4));
            string clone = draft.Selected;
            var run = PuzzleRun.FromDraft(draft);
            Check(run.Step(Vector2Int.right, out _) && run.Solved && run.ObjectState(run.ActiveId, decoration) == "changed" &&
                run.ObjectState(run.ActiveId, clone) == "changed" && RoomFile.Objects(run.Room).Count(item => item["stateProfile"] != null) == 2,
                "普通装饰可由节点图切换外观，定义放置后分别执行到两个稳定 ID");

            draft.NewDefaultRoom();
            draft.Select(new Vector2Int(3,2));
            string boxId = draft.Selected;
            draft.SetStateProfile(ObjectStateProfile.ForestProfile());
            var steps = PuzzleRun.FromDraft(draft);
            Check(steps.Step(Vector2Int.right, out var pushed) && pushed == boxId && steps.Solved &&
                steps.ObjectState(steps.ActiveId,boxId) == "logs", "把树推上目标的那一步仍用原箱子身份正确判定目标");
            JToken Remnant() => steps.Snapshot()["rooms"][steps.ActiveId]["objectStates"][boxId]["remnantCell"];
            var remnant = Remnant();
            Check(steps.Step(Vector2Int.right, out pushed) && pushed == boxId && !steps.Solved &&
                JToken.DeepEquals(remnant, Remnant()), "第二次推动保持原木，原格树桩不随箱移动且不挡路");
            Check(!steps.Step(Vector2Int.right, out _) && steps.ObjectState(steps.ActiveId,boxId) == "logs", "箱子撞墙仍拒绝推动且不改状态");
        }

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity");
            SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Hook() => EditorApplication.update += Tick;

        static void Tick()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(Key,false) || frame == Time.frameCount) return;
            frame = Time.frameCount;
            int result = 0;
            try { if ((steps ??= Verify()).MoveNext()) return; }
            catch (Exception error) { Debug.LogException(error); result = 1; }
            SessionState.EraseBool(Key);
            EditorApplication.Exit(result);
        }

        static IEnumerator Verify()
        {
            yield return null;
            yield return null;
            var editor = Object.FindObjectOfType<LayoutHost>();
            var ui = Object.FindObjectOfType<RuntimeLayoutUI>();
            editor.Panel.Draft.Open(Mixed,true);
            ui.Act("纸箱");
            yield return null;
            var labels = ui.assetEntries.GetComponentsInChildren<Text>().Select(text => text.text).ToArray();
            Check(labels.Contains("仓库木箱") && labels.Contains("工厂货箱") && labels.Contains("森林原木") && labels.Contains("导入箱子模型"),
                "正式游戏内编辑器的箱子分类列出三种主题素材及导入入口");
            var import = ui.assetEntries.GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<Text>().text == "导入箱子模型");
            import.onClick.Invoke();
            editor.Panel.Filename = Environment.GetEnvironmentVariable("SOKOBAN_APPEARANCE_GLB");
            editor.Panel.SubmitFile();
            float importDeadline = Time.realtimeSinceStartup + 10;
            while (editor.Panel.Operation != null && Time.realtimeSinceStartup < importDeadline) yield return null;
            yield return null;
            Check(editor.Panel.Operation == null && editor.Panel.Draft.BrushAsset != null &&
                editor.Panel.Draft.CategoryAssets(RoomTool.Box).Contains(editor.Panel.Draft.BrushAsset) &&
                ui.assetEntries.GetComponentsInChildren<Text>().Any(text => text.text == "Carrier"), "真实分类按钮导入 GLB 并选为箱子笔刷");
            ui.Act("选地形");
            ui.Act("墙壁");
            yield return null;
            labels = ui.assetEntries.GetComponentsInChildren<Text>().Select(text => text.text).ToArray();
            Check(labels.Contains("仓库木墙") && labels.Contains("工厂金属墙") && labels.Contains("森林青苔石墙"), "运行端墙壁分类展示三种独立模型");
            ui.Act("选物件");
            yield return null;
            var other = ui.objectCards.GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<Text>().text == "其他");
            other.onClick.Invoke();
            yield return null;
            Check(ui.assetEntries.GetComponentsInChildren<Text>().Any(text => text.text == "森林木篱笆"), "范围独立的其他分类列出森林篱笆");
            editor.Panel.Draft.Open(Forest,true);
            editor.StartTrial();
            yield return null;
            yield return null;
            var host = Object.FindObjectOfType<PuzzleHost>();
            Check(host && host.IsTrial, "Windows 同入口 RuntimeEditor 读取森林配置并启动试玩");
            if (host.Run.PendingItem != null) host.panels.Back();
            yield return null;
            var initial = host.Run.Boxes.ToDictionary(box => box.Key, box => box.Value);
            var room = host.transform.Find("forest");
            foreach (var box in initial)
                Check(host.Run.ObjectState("forest",box.Key) == "tree" && room.Find(box.Key).Find("状态 · tree").gameObject.activeSelf, "初始完整树木 " + box.Key);

            var input = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            var solver = RoomSolution.Check(host.Run.Room, host.Run.Player - host.Run.Origin);
            string moved = null;
            foreach (char command in solver.Detail)
            {
                int index = "NESW".IndexOf(command);
                if (index < 0) continue;
                Check(host.Move(input[index]), "走向首个可推动树木 " + command + "；玩家 " + host.Run.Player + "，视角 " + host.Run.Heading);
                while (host.Busy) yield return null;
                moved = initial.Keys.FirstOrDefault(id => host.Run.ObjectState("forest", id) == "logs");
                if (moved != null) break;
            }
            Check(moved != null && host.Run.Boxes[moved] != initial[moved] && room.Find(moved).Find("状态 · logs").gameObject.activeSelf &&
                room.Find("遗留物 · " + moved) != null, "首次推动产生原木和原格树桩，箱子 ID 未变");
            var before = host.Run.Snapshot();
            var reloaded = PuzzleRun.FromDraft(editor.Panel.Draft);
            reloaded.Restore(before);
            Check(reloaded.ObjectState("forest",moved) == "logs" && JToken.DeepEquals(before,reloaded.Snapshot()), "存读还原原木状态、树桩与稳定箱子坐标");
            Check(host.UseItem(), "同一恢复道具触发配置的恢复转换");
            float deadline = Time.realtimeSinceStartup + 8;
            bool decay = false, regrow = false;
            bool recoveredSnapshot = false;
            while (host.Run.Recovering && Time.realtimeSinceStartup < deadline)
            {
                decay |= room.Find("原木腐朽")?.gameObject.activeSelf == true;
                regrow |= room.Find("恢复格再生")?.gameObject.activeSelf == true;
                if (decay && !recoveredSnapshot)
                {
                    var during = host.Run.Snapshot();
                    reloaded.Restore(during);
                    Check(reloaded.Recovering && JToken.DeepEquals(during, reloaded.Snapshot()), "恢复中段保存重读保留状态、树桩及播放进度");
                    recoveredSnapshot = true;
                }
                yield return null;
            }
            Check(decay && regrow && host.Run.Boxes[moved] == initial[moved] && host.Run.ObjectState("forest",moved) == "tree" &&
                room.Find(moved).Find("状态 · tree").gameObject.activeSelf, "实际看到 Blender 腐朽与恢复格生长对象，结束后回到完整树木");
            yield return null;
            Check(room.Find("遗留物 · " + moved) == null, "恢复完成移除通行树桩");
        }
    }
}
