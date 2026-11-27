/**
 * @file AppearanceStateVerification.cs
 * @brief Prepare 用 Core 类型化模型核对外观引用、混搭房间保存重开、导入模型与对象模板实例、对象状态推动，并导出配置好对象状态的森林房；Run 驱动真实 RuntimeEditor 的素材卡、试玩与恢复，Tick 逐帧推进并退出批处理编辑器。
 * Prepare：输出目录取自环境变量 SOKOBAN_APPEARANCE_VERIFY，导入模型取自 SOKOBAN_APPEARANCE_GLB，运行方式不变。
 * Playable：给空白默认房间补上箱子、目标与恢复道具，使其通过发布级校验。
 * ForestExport：经 ObjectStateEditor 与 SetStateTable 给两个森林箱写入 Core.ObjectStateTable，导出后重读核对转换与残留物外观。
 * MixedRoom：用 AppearanceRef 放置混搭外观，核对草稿重开、换主题冻结旧主题默认外观、一次撤销与重做、保存重开。
 * ImportedModel：导入 GLB 作为自定义物件放进房间，独立导出后资源描述符随房间保存。
 * TemplateInstance：自定义物件存为对象模板，实例只存模板 ID 加覆盖项，由模板的行为图切换状态。
 * ForestPush：把树推上目标时仍用原箱子身份判定，第二次推动保持原木，撞墙拒绝推动且不改状态。
 * Run、Hook、Tick、Verify：进入 Play 模式后逐帧执行素材卡、森林试玩、存读与恢复检查；Verify 按固定步序穿过藤门并推动树木。
 * StateOf、ViewState：读取运行态里的对象状态，未写入时回落到状态表的初始状态。
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Sokoban.Core;
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

        static void Playable(RoomDraft draft)
        {
            draft.Place(true, new Cell(3, 2), "box-qa");
            draft.Place(false, new Cell(4, 2), "target-qa");
            VerificationFixtures.EnsureRecovery(draft);
        }

        static string StateOf(PuzzleRun run, string roomId, string objectId)
        {
            var states = run.ObjectStatesIn(roomId);
            if (states != null && states.TryGetValue(objectId, out var state)) return state;
            return run.Models[roomId].ObjectStates.TryGetValue(objectId, out var table) ? table.Initial : null;
        }

        static string ViewState(PuzzleRun run, Transform room, string objectId)
        {
            var states = run.ObjectStatesIn(run.ActiveId);
            if (states != null && states.TryGetValue(objectId, out var state)) return state;
            return room.Find(objectId).GetComponent<ObjectStateView>().Table.Initial;
        }

        static bool IsForestTable(ObjectStateTable table) =>
            table != null && table.Initial == "tree" && table.States.SequenceEqual(new[] { "tree", "logs" }) && table.Transitions.Count == 2 &&
            table.Transitions[0].From == "tree" && table.Transitions[0].Trigger == TriggerIds.FirstPush && table.Transitions[0].To == "logs" && table.Transitions[0].Remnant?.Id == "TreeStump" &&
            table.Transitions[1].From == "logs" && table.Transitions[1].Trigger == TriggerIds.RecoveryEnd && table.Transitions[1].To == "tree" && table.Transitions[1].Remnant == null;

        static void ForestExport(RoomDraft draft)
        {
            draft.Open(Path.Combine(Application.streamingAssetsPath, "World", "forest.room.json"), true);
            var boxes = draft.Room.ObjectsOf(ObjectCategories.Box).ToArray();
            Check(boxes.Length == 2, "正式森林房有两个箱子");
            foreach (var box in boxes)
            {
                draft.Select(box.Cell);
                var editor = new ObjectStateEditor(draft);
                editor.UseForestPreset();
                editor.Apply(draft);
            }
            draft.ExportRoom(Forest);
            var read = RoomFile.Load(Forest);
            Check(read.Room.ObjectsOf(ObjectCategories.Box).All(b => IsForestTable(read.ObjectStates.TryGetValue(b.Id, out var table) ? table : null)) && read.ObjectStates.Count == 2,
                "Unity 房间工具配置两个正式森林箱，导出后对象状态表、触发器 firstPush 与 recoveryEnd 及残留物外观完整保留");
        }

        static void Restyle(RoomDraft draft, Cell cell, string id, int variant = 0)
        {
            draft.Select(cell);
            draft.ReplaceAppearance(id, variant);
        }

        static void MixedRoom(RoomDraft draft)
        {
            draft.NewDefaultRoom();
            string theme = draft.Theme;
            VerificationFixtures.EnsureRecovery(draft);
            Restyle(draft, new Cell(1, 6), "FactoryWall");
            Restyle(draft, new Cell(2, 6), "ForestWall");
            Restyle(draft, new Cell(2, 4), "forestFloor", 1);
            Restyle(draft, new Cell(3, 4), "factoryFloor", 2);
            draft.Tool = RoomTool.Box;
            draft.ChooseAppearance("Logs", ObjectCategories.Box);
            draft.Click(new Cell(2, 4));
            string log = draft.Selected;
            draft.Tool = RoomTool.Target;
            draft.ChooseAppearance("ForestTarget", ObjectCategories.Target);
            draft.Click(new Cell(3, 4));
            draft.Tool = RoomTool.Decoration;
            draft.ChooseAppearance("ForestFence", AppearanceLibrary.Decoration);
            draft.Click(new Cell(8, 3));
            Check(draft.Room.AppearanceAt(new Cell(1, 6)).Id == "FactoryWall" && draft.Room.AppearanceAt(new Cell(2, 6)).Id == "ForestWall" &&
                draft.Room.AppearanceAt(new Cell(2, 4)).Equals(new AppearanceRef("forestFloor", 1)) && draft.Room.AppearanceAt(new Cell(3, 4)).Equals(new AppearanceRef("factoryFloor", 2)) &&
                draft.Find(log).Appearance.Id == "Logs" && draft.Model.Decorations.Single().Appearance.Id == "ForestFence", "混搭房间的墙、地板变体、箱子、目标与墙外装饰各自保存 AppearanceRef");

            string before = RoomCodec.Encode(draft.Model);
            draft.Recover(draft.Snapshot());
            Check(before == RoomCodec.Encode(draft.Model) && draft.Model.Decorations.Single().Appearance.Id == "ForestFence", "草稿快照重开后整房外观引用与装饰一致");

            var wallsBefore = draft.Room.AppearanceAt(new Cell(1, 6));
            draft.SetTheme("forest");
            var frozen = AppearanceLibrary.Default(TerrainCategories.Floor, theme);
            Check(draft.Theme == "forest" && draft.Find(log).Appearance.Id == "Logs" && draft.Room.AppearanceAt(new Cell(1, 6)).Equals(wallsBefore) &&
                draft.Room.AppearanceAt(new Cell(2, 4)).Equals(new AppearanceRef("forestFloor", 1)) && draft.Room.AppearanceAt(new Cell(1, 1)).Equals(frozen), "更换主题保留已放外观，未写外观的格子冻结为旧主题默认值");
            string themed = RoomCodec.Encode(draft.Model);
            draft.Undo();
            Check(before == RoomCodec.Encode(draft.Model) && draft.Theme == theme && draft.Room.AppearanceAt(new Cell(1, 1)) == null, "换主题一次撤销回到旧主题与未冻结外观");
            draft.Redo();
            Check(themed == RoomCodec.Encode(draft.Model) && draft.Theme == "forest", "换主题一次重做");

            draft.ExportRoom(Mixed);
            draft.Open(Mixed, false);
            Check(File.ReadAllText(Mixed) == themed && draft.Find(log).Appearance.Id == "Logs" && themed == RoomCodec.Encode(draft.Model), "混搭单房保存导出后重开，文本与外观引用一致");
        }

        static Cell FreeFloor(RoomDraft draft, string categoryId)
        {
            for (int y = 0; y < draft.Room.Height; y++)
                for (int x = 0; x < draft.Room.Width; x++)
                {
                    var cell = new Cell(x, y);
                    if (draft.PlacementError(categoryId, cell) == null) return cell;
                }
            throw new InvalidOperationException("房间没有可放置的空地板格");
        }

        static void ImportedModel(RoomDraft draft, string modelPath)
        {
            draft.NewDefaultRoom();
            Playable(draft);
            string asset = draft.ImportAsset(modelPath, "model");
            Check(draft.AssetDefinition(asset)?.Kind == AssetKinds.Model && draft.Model.Assets.Count(a => a.Id == asset) == 1, "导入 GLB 登记为本房模型资源描述符，重复导入不重复登记");
            draft.SelectAsset(asset, ObjectCategories.Custom);
            Check(draft.BrushCategory == ObjectCategories.Custom && draft.BrushAsset == asset, "自定义页签选中导入模型作为画刷");
            var cell = FreeFloor(draft, ObjectCategories.Custom);
            draft.PlaceAsset(cell);
            var placed = draft.Room.ObjectAt(cell);
            Check(placed != null && placed.CategoryId == ObjectCategories.Custom && placed.Appearance.Id == asset && draft.Room.TerrainAt(cell).CategoryId == TerrainCategories.Floor,
                "导入模型作为自定义物件占房内格，外观引用指向资源 ID");
            string standalone = Path.Combine(Folder, "reuse.room.json");
            draft.ExportRoom(standalone);
            var read = RoomFile.Load(standalone);
            Check(read.Assets.Any(a => a.Id == asset && a.Kind == AssetKinds.Model) && read.Room.Objects.Any(o => o.CategoryId == ObjectCategories.Custom && o.Appearance?.Id == asset),
                "独立导出的房间文件自带模型资源描述符与自定义物件外观，不依赖其他房间");
            draft.Undo();
            Check(draft.Room.ObjectAt(cell) == null && draft.AssetDefinition(asset) != null, "撤销放置只移除物件，已导入资源仍保留在本房资源表");
            draft.Redo();
        }

        static void TemplateInstance(RoomDraft draft, string modelPath)
        {
            draft.NewDefaultRoom();
            Playable(draft);
            string asset = draft.ImportAsset(modelPath, "model");
            draft.SelectAsset(asset, ObjectCategories.Custom);
            var sourceCell = FreeFloor(draft, ObjectCategories.Custom);
            draft.PlaceAsset(sourceCell);
            string source = draft.PlacedObject;
            draft.Select(sourceCell);
            draft.SetStateTable(new ObjectStateTable("idle", new[] { "idle", "ForestFence" }, new[] { new StateTransition("idle", TriggerIds.RoomSolved, "ForestFence") }));
            var graph = new BehaviorGraph("switch", null,
                new[] { ObjectBehavior.Node(TriggerIds.RoomSolved, null, "start"), ObjectBehavior.Node(ObjectBehavior.SetState, new Dictionary<string, string> { ["state"] = "ForestFence" }, "set") },
                new[] { new BehaviorEdge("start", ObjectBehavior.Out, "set", ObjectBehavior.In) });
            draft.SetBehavior(sourceCell, graph);
            Check(draft.Model.ObjectStates.ContainsKey(source) && draft.Model.Graphs.Single().OwnerId == source && !ObjectBehavior.Issues(draft.Model).Any(), "普通自定义物件带对象状态表与 setState 行为图，校验无问题");
            draft.Select(sourceCell);
            draft.DefineObject("可切换的物件");
            string templateId = draft.Model.Templates.Keys.Single();
            draft.SelectDefinition(templateId);
            var cloneCell = FreeFloor(draft, ObjectCategories.Custom);
            draft.PlaceAsset(cloneCell);
            string clone = draft.PlacedObject;
            var model = draft.Model;
            var instance = model.Instances.Single();
            Check(instance.Id == clone && instance.TemplateId == templateId && instance.Overrides.Count == 0, "实例只存模板 ID 加覆盖项，覆盖项为空");
            Check(!model.ObjectStates.ContainsKey(clone) && !model.Graphs.Any(g => g.OwnerId == clone) && model.Graphs.Count == 1 && model.ObjectStates.Count == 1, "实例不拷贝模板的对象状态表与行为图，房间自有数据仍只有源物件一份");
            Check(ReferenceEquals(ObjectStateProfile.StatesOf(model, clone), model.Templates[templateId].States), "实例的对象状态表直接解析为模板的状态表");
            var resolved = ObjectBehavior.GraphsOf(model, clone).Single();
            Check(resolved.OwnerId == clone && resolved.Id.StartsWith(clone + "/") && ObjectBehavior.Effects(resolved, TriggerIds.RoomSolved).Single().Kind == ObjectBehavior.SetState, "实例的行为图由模板解析并绑定到实例 ID，roomSolved 触发 setState");
            Check(!ObjectBehavior.Issues(model).Any() && !RoomDraft.DefinitionIssues(model).Any(), "模板与实例通过行为图及模板校验");
            Check(!draft.CanEditStateProfile, "模板实例的状态表在模板中编辑，房内不可直接改");
            var reread = RoomCodec.Decode(RoomCodec.Encode(model));
            Check(reread.Instances.Single().TemplateId == templateId && reread.Instances.Single().Overrides.Count == 0 && reread.Templates[templateId].Graphs.Count == 1 && !reread.ObjectStates.ContainsKey(clone), "保存重读后实例仍只存模板 ID，模板内容只在模板中");
        }

        static void ForestPush(RoomDraft draft)
        {
            draft.NewDefaultRoom();
            var boxCell = new Cell(3, 2);
            draft.Place(true, boxCell, "box-tree");
            draft.Place(false, new Cell(4, 2), "target-qa");
            draft.PlaceRecoveryDevice(new RecoveryDevice("recovery-qa", RecoveryDeviceKind.Tree,
                new Cell(-1, draft.Room.Height / 2), 1, "forest-decay", RecoveryItem.DefaultDuration), replaceExisting: true);
            draft.Select(boxCell);
            draft.SetStateTable(ObjectStateProfile.ForestTable());
            var run = PuzzleRun.FromDraft(draft);
            string room = run.ActiveId;
            Check(StateOf(run, room, "box-tree") == "tree", "推动前箱子处于初始状态 tree");
            Check(run.Step(Vector2Int.right, out var pushed) && pushed == "box-tree" && run.Solved && StateOf(run, room, "box-tree") == "logs",
                "把树推上目标的那一步仍用原箱子身份正确判定目标，firstPush 转换为 logs");
            string signature = string.Join(";", run.Snapshot().ObjectStates[room].OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => s.Key + "=" + s.Value));
            Check(run.Step(Vector2Int.right, out pushed) && pushed == "box-tree" && !run.Solved && StateOf(run, room, "box-tree") == "logs" &&
                signature == string.Join(";", run.Snapshot().ObjectStates[room].OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => s.Key + "=" + s.Value)),
                "第二次推动保持原木，对象状态与存档签名不变，残留物固定在初始格不随箱移动");
            var before = run.Player;
            Check(!run.Step(Vector2Int.right, out _) && run.Player == before && StateOf(run, room, "box-tree") == "logs", "箱子撞墙仍拒绝推动且不改状态");
        }

        public static void Prepare()
        {
            Directory.CreateDirectory(Folder);
            var draft = RoomLayoutWindow.Open().Panel.Draft;
            ForestExport(draft);
            MixedRoom(draft);
            string modelPath = Environment.GetEnvironmentVariable("SOKOBAN_APPEARANCE_GLB");
            ImportedModel(draft, modelPath);
            TemplateInstance(draft, modelPath);
            ForestPush(draft);
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
            if (!EditorApplication.isPlaying || !SessionState.GetBool(Key, false) || frame == Time.frameCount) return;
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
            editor.Panel.Draft.Open(Mixed, true);
            ui.Act("纸箱");
            yield return null;
            var labels = ui.assetEntries.GetComponentsInChildren<Text>().Select(text => text.text).ToArray();
            Check(labels.Contains("仓库木箱") && labels.Contains("工厂货箱") && labels.Contains("森林原木") && labels.Contains("＋ 新增箱子模型"),
                "正式游戏内编辑器的箱子分类列出三种主题素材及导入入口");
            var import = ui.assetEntries.GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<Text>().text == "＋ 新增箱子模型");
            import.onClick.Invoke();
            editor.Panel.Filename = Environment.GetEnvironmentVariable("SOKOBAN_APPEARANCE_GLB");
            editor.Panel.SubmitFile();
            float importDeadline = Time.realtimeSinceStartup + 10;
            while (editor.Panel.Operation != null && Time.realtimeSinceStartup < importDeadline) yield return null;
            yield return null;
            Check(editor.Panel.Operation == null && editor.Panel.Draft.BrushAsset != null &&
                ui.assetEntries.GetComponentsInChildren<Text>().Any(text => text.text == "Carrier"), "真实分类按钮导入 GLB 并选为箱子笔刷");
            ui.Act("选地形");
            ui.Act("墙壁");
            yield return null;
            labels = ui.assetEntries.GetComponentsInChildren<Text>().Select(text => text.text).ToArray();
            Check(labels.Contains("仓库木墙") && labels.Contains("工厂金属墙") && labels.Contains("森林青苔石墙"), "运行端墙壁分类展示三种独立模型");
            var wallCard = ui.assetEntries.GetComponentsInChildren<Button>().First(button => button.GetComponentInChildren<Text>().text == "仓库木墙");
            wallCard.onClick.Invoke();
            yield return null;
            Check(editor.Panel.Draft.Tool == RoomTool.Wall && editor.Panel.Draft.BrushAppearance != null, "点素材卡后工具切到画墙且画刷非空，不回到选择工具");
            ui.Act("选物件");
            yield return null;
            var other = ui.objectCards.GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<Text>().text == "其他");
            other.onClick.Invoke();
            yield return null;
            Check(ui.assetEntries.GetComponentsInChildren<Text>().Any(text => text.text == "森林木篱笆"), "范围独立的其他分类列出森林篱笆");

            editor.Panel.Draft.Open(Forest, true);
            editor.StartTrial();
            yield return null;
            yield return null;
            var host = Object.FindObjectOfType<PuzzleHost>();
            Check(host && host.IsTrial, "Windows 同入口 RuntimeEditor 读取森林配置并启动试玩");
            if (host.Run.PendingItem != null) host.panels.Back();
            yield return null;
            string roomId = host.Run.ActiveId;
            var initial = host.Run.Boxes.ToDictionary(box => box.Key, box => box.Value);
            var room = host.transform.Find(roomId);
            foreach (var box in initial)
                Check(ViewState(host.Run, room, box.Key) == "tree" && room.Find(box.Key).Find("状态 · tree").gameObject.activeSelf, "初始完整树木 " + box.Key);

            foreach (var step in new[] { Vector2Int.up, Vector2Int.up, Vector2Int.right })
            {
                Check(host.Move(step), "走向森林藤门，玩家 " + host.Run.Player);
                while (host.Busy) yield return null;
            }
            Check(!host.Move(Vector2Int.right) && host.UseItem(), "藤门挡路，使用恢复装置开门");
            for (int i = 0; i < 2; i++)
            {
                Check(host.Move(Vector2Int.right), "穿过藤门，玩家 " + host.Run.Player);
                while (host.Busy) yield return null;
            }
            while (host.Run.Recovering) yield return null;
            Check(host.Move(Vector2Int.right), "推动藤门另一侧的树木");
            while (host.Busy) yield return null;
            string moved = initial.Keys.SingleOrDefault(id => StateOf(host.Run, roomId, id) == "logs");
            Check(moved != null && host.Run.Boxes[moved] != initial[moved] && room.Find(moved).Find("状态 · logs").gameObject.activeSelf && room.Find("残留物 · " + moved) != null,
                "首次推动产生原木和原格树桩，箱子 ID 未变");
            var before = host.Run.Snapshot();
            string beforeJson = SaveCodec.ToJson(before);
            var reloaded = PuzzleRun.FromDraft(editor.Panel.Draft);
            reloaded.Restore(before);
            Check(StateOf(reloaded, roomId, moved) == "logs" && beforeJson == SaveCodec.ToJson(reloaded.Snapshot()), "存读还原原木状态与稳定箱子坐标，存档 JSON 一致");
            Check(host.UseItem(), "同一恢复道具触发配置的恢复转换");
            float deadline = Time.realtimeSinceStartup + 8;
            bool decay = false, regrow = false, recoveredSnapshot = false;
            while (host.Run.Recovering && Time.realtimeSinceStartup < deadline)
            {
                decay |= room.Find(moved + "-decay")?.gameObject.activeSelf == true;
                regrow |= room.Find(moved + "-tree")?.gameObject.activeSelf == true;
                if (decay && !recoveredSnapshot)
                {
                    var during = host.Run.Snapshot();
                    string duringJson = SaveCodec.ToJson(during);
                    reloaded.Restore(during);
                    Check(reloaded.Recovering && duringJson == SaveCodec.ToJson(reloaded.Snapshot()), "恢复中段保存重读保留状态、树桩及播放进度");
                    recoveredSnapshot = true;
                }
                yield return null;
            }
            Check(decay && regrow && host.Run.Boxes[moved] == initial[moved] && StateOf(host.Run, roomId, moved) == "tree" &&
                room.Find(moved).Find("状态 · tree").gameObject.activeSelf, "实际看到腐朽碎片与初始格生长的树，结束后 recoveryEnd 回到完整树木");
            yield return null;
            Check(room.Find("残留物 · " + moved) == null, "恢复完成移除通行树桩");
        }
    }
}
