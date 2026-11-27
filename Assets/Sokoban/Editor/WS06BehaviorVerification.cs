/**
 * @file WS06BehaviorVerification.cs
 * @brief Run 用 Core.BehaviorGraph 构造两面内墙的夹具，绑定、保存并重开，断言身份、图的所有者、连线与实际触发结果；Tick 逐帧推进 Play 模式检查。
 * Fixture：放两列内墙，使图的主体和对照墙在默认镜头中可见，再放入箱子和目标。
 * RecoverySelection：验证当前房间装置启动、存档读回和临近结束的恢复续播。
 * Edges：把图内局部 ID 解析为 节点类型.端口 的语义连线。
 * NonInteractGraphs(draft, subject)：给真实试玩夹具绑定首次推动、解题、恢复开始和结束的可视及 Lua 动作。
 * StepsTo(run, goal)：按地板与当前箱位搜索一个无需推箱的测试路线。
 * Trial(path)：由真实 PuzzleHost 操作推箱、恢复与重推，核对事件派发、渲染器、规则及存档数据。
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
    public static class WS06BehaviorVerification
    {
        const string Key = "WS06Behavior.Path";
        static IEnumerator trial;
        static int frame = -1;

        static void Check(bool ok, string detail)
        {
            if (!ok) throw new InvalidOperationException(detail);
            Debug.Log("WS06BEHAVIOR PASS " + detail);
        }

        static string[] Edges(BehaviorGraph graph)
        {
            var kinds = graph.Nodes.ToDictionary(n => n.Id, n => n.Kind);
            return graph.Edges.Select(e => kinds[e.FromNode] + "." + e.FromPort + "->" + kinds[e.ToNode] + "." + e.ToPort).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        }

        static void Fixture(RoomDraft draft, Cell a, Cell b)
        {
            Check(draft.Brush(new[] { a, b, a + Cell.Up, b + Cell.Up }, RoomTool.Wall) == null,
                "constructed two interior wall fronts with solid cells behind them");
            draft.Place(true, new Cell(3, 2), "box-a");
            draft.Place(false, new Cell(4, 2), "target-a");
        }

        static void RecoverySelection()
        {
            var draft = new LayoutPanel().Draft;
            var cell = new Cell(3, 3);
            Fixture(draft, cell, new Cell(4, 3));
            var device = draft.Model.RecoveryDevice;
            RecoveryDeviceCommands.SetDetails(new DraftHolder(draft.Model), .4f, device.ItemName,
                device.ItemDescription, device.ItemAcquiredHint).Apply();
            var run = PuzzleRun.FromDraft(draft);
            var initial = run.Snapshot();
            Check(initial.Items.SequenceEqual(new[] { run.ActiveId }) && initial.SelectedItem == run.ActiveId,
                "inventory identifies the visited room");
            Check(run.BeginRecovery() && !run.BeginRecovery(), "current room device starts once");
            Check(!run.AdvanceRecovery(.39996f),
                "a short recovery remains active just before its end");
            var almostDone = run.Snapshot();
            var replay = SaveCodec.Decode(SaveCodec.Encode(almostDone), almostDone.WorldId, almostDone.Hashes);
            Check(replay.Status == SaveStatus.Ok && replay.Data.Presentation.RecoveryDeviceId == device.Id
                && replay.Data.Presentation.RecoveryT == .999f,
                "unfinished short recovery saves without rounding to completion");
            var restored = PuzzleRun.FromDraft(draft);
            restored.Restore(replay.Data);
            Check(restored.Recovering && restored.Snapshot().Presentation.RecoveryDeviceId == device.Id
                && restored.AdvanceRecovery(.01f), "read back the short recovery and complete it once");
            Check(restored.Snapshot().Presentation.RecoveryDeviceId == null && !restored.AdvanceRecovery(1f),
                "completion clears the saved device and cannot complete twice");
        }

        static void NonInteractGraphs(RoomDraft draft, Cell subject)
        {
            var sound = ObjectBehavior.Node(BehaviorNodeKinds.PlaySound, new Dictionary<string, string> { ["sound"] = "push" }, "sound");
            draft.SetBehavior(subject, new BehaviorGraph("solved-sound", null,
                new[] { ObjectBehavior.Node(TriggerIds.RoomSolved, null, "solve"), sound },
                new[] { new BehaviorEdge("solve", "out", "sound", "in") }));
            var nodes = new[]
            {
                ObjectBehavior.Node(TriggerIds.OnInteract, null, "interact"),
                ObjectBehavior.Node(BehaviorNodeKinds.ShowText, new Dictionary<string, string> { ["text"] = "interaction" }, "interact-text"),
                ObjectBehavior.Node(TriggerIds.FirstPush, null, "push"),
                ObjectBehavior.Node(ObjectBehavior.Lua, new Dictionary<string, string> { ["script"] = "self.showText('first push')" }, "push-text"),
                ObjectBehavior.Node(TriggerIds.RecoveryStart, null, "start"),
                ObjectBehavior.Node(BehaviorNodeKinds.ShowText, new Dictionary<string, string> { ["text"] = "recovery start" }, "start-text"),
                ObjectBehavior.Node(TriggerIds.RecoveryEnd, null, "end"),
                ObjectBehavior.Node(ObjectBehavior.Lua, new Dictionary<string, string> { ["script"] = "self.setVisible(false); self.showText('recovery end')" }, "end-effect"),
            };
            draft.SetBehavior(new Cell(3, 2), new BehaviorGraph("box-events", null, nodes,
                new[] { new BehaviorEdge("interact", "out", "interact-text", "in"), new BehaviorEdge("push", "out", "push-text", "in"),
                    new BehaviorEdge("start", "out", "start-text", "in"), new BehaviorEdge("end", "out", "end-effect", "in") }));
        }

        public static void Run()
        {
            var worldDraft = new LayoutPanel().Draft;
            worldDraft.Open(Path.Combine(Application.streamingAssetsPath, "World", "main.world.json"), true);
            Check(worldDraft.World.Rooms.All(room =>
                worldDraft.Catalog[room.LevelId].Room.RecoveryDeviceFloor is Cell floor &&
                worldDraft.RoomAt(room.Origin + floor) == room.LevelId), "world hit testing resolves all three external recovery floors");
            worldDraft.UseRoom("warehouse");
            var warehouseAnchor = worldDraft.Room.RecoveryDeviceFloor;
            string conflict = null;
            try { worldDraft.MoveRecoveryDevice(new Cell(worldDraft.Room.Width, 3)); }
            catch (InvalidOperationException error) { conflict = error.Message; }
            Check(conflict != null && conflict.Contains("占地重叠") && worldDraft.Room.RecoveryDeviceFloor == warehouseAnchor,
                "moving a recovery floor onto the neighboring room is rejected atomically");

            var draft = new LayoutPanel().Draft;
            var a = new Cell(3, 3);
            var b = new Cell(4, 3);
            Fixture(draft, a, b);
            RecoverySelection();
            string baseline = RoomCodec.Encode(draft.Model);
            draft.SetBehavior(a, ObjectBehavior.Template());
            string owner = draft.OwnerAt(a);
            string bound = RoomCodec.Encode(draft.Model);
            Check(!string.IsNullOrEmpty(owner) && draft.Model.Graphs.Single().OwnerId == owner && draft.Room.Ids.Contains(owner), "first wall identity and graph binding " + owner);
            draft.Undo();
            Check(draft.OwnerAt(a) == null && draft.Model.Graphs.Count == 0 && !draft.Room.Ids.Contains(owner) && baseline == RoomCodec.Encode(draft.Model), "single undo removes graph and identity");
            draft.Redo();
            Check(bound == RoomCodec.Encode(draft.Model) && draft.OwnerAt(a) == owner, "redo restores graph and original identity");

            var original = draft.Model.Graphs.Single();
            var copy = ObjectBehavior.CopyFor(original, null);
            var expected = new[] { "condition.true->setVisible.in", "roomSolved.out->condition.in" };
            Check(copy.Id != original.Id && copy.OwnerId == null && copy.Nodes.Count == 3 && !copy.Nodes.Select(n => n.Id).Intersect(original.Nodes.Select(n => n.Id)).Any()
                && Edges(copy).SequenceEqual(expected) && Edges(original).SequenceEqual(expected), "copied graph has fresh IDs and both named edges resolve inside each graph");
            var disabled = new BehaviorGraph(copy.Id, null, copy.Nodes.Select(n => n.Kind == ObjectBehavior.Condition ? ObjectBehavior.WithParameter(n, "enabled", "false") : n).ToList(), copy.Edges);
            var hide = ObjectBehavior.Effects(original, TriggerIds.RoomSolved).Single();
            Check(hide.Kind == ObjectBehavior.SetVisible && ObjectBehavior.Parameter(hide, "visible") == "false" && !ObjectBehavior.Effects(disabled, TriggerIds.RoomSolved).Any(),
                "true emits hide, false emits no effect; editing copy leaves original independent");
            draft.SetBehavior(b, disabled);
            Check(draft.OwnerAt(b) != null && draft.OwnerAt(b) != owner && draft.Model.Graphs.Last().OwnerId == draft.OwnerAt(b), "copied graph bound to independent wall");

            NonInteractGraphs(draft, a);
            string folder = Environment.GetEnvironmentVariable("WS06_BEHAVIOR_EVIDENCE") ?? Path.GetFullPath("Logs/WS06Behavior");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "two-walls.room.json");
            string design = RoomCodec.Encode(draft.Model);
            draft.Save(path, false);
            draft.Open(path, false);
            Check(design == File.ReadAllText(path) && design == RoomCodec.Encode(draft.Model), "formal save and reopen preserve whole room including graph IDs, owners, edges and conditions");
            EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity");
            SessionState.SetString(Key, path);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Hook() { EditorApplication.update += Tick; }

        static void Tick()
        {
            string path = SessionState.GetString(Key, "");
            if (path.Length == 0 || !Application.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try { if (trial == null) trial = Trial(path); if (trial.MoveNext()) return; SessionState.EraseString(Key); EditorApplication.Exit(0); }
            catch (Exception ex) { SessionState.EraseString(Key); Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        static IEnumerable<Vector2Int> StepsTo(PuzzleRun run, Vector2Int goal)
        {
            var start = run.Player;
            var boxes = new HashSet<Vector2Int>(run.Boxes.Values);
            var parents = new Dictionary<Vector2Int, Vector2Int> { [start] = start };
            var pending = new Queue<Vector2Int>();
            pending.Enqueue(start);
            while (pending.Count > 0 && !parents.ContainsKey(goal))
            {
                var current = pending.Dequeue();
                foreach (var direction in Cell.SearchOrder)
                {
                    var next = current + new Vector2Int(direction.X, direction.Y);
                    var local = next - run.Origin;
                    if (parents.ContainsKey(next) || boxes.Contains(next) ||
                        run.Room.Room.TerrainAt(new Cell(local.x, local.y)).CategoryId != TerrainCategories.Floor) continue;
                    parents.Add(next, current);
                    pending.Enqueue(next);
                }
            }
            if (!parents.ContainsKey(goal)) throw new InvalidOperationException("No test route to push position");
            var steps = new Stack<Vector2Int>();
            for (var cell = goal; cell != start; cell = parents[cell]) steps.Push(cell - parents[cell]);
            return steps;
        }

        static IEnumerator Trial(string path)
        {
            yield return null; yield return null;
            var editor = Object.FindObjectOfType<LayoutHost>();
            editor.Panel.Draft.Open(path, false);
            var runtimeUi = Object.FindObjectOfType<RuntimeLayoutUI>();
            var inspectorDraft = editor.Panel.Draft;
            var inspectedDevice = inspectorDraft.Model.RecoveryDevice;
            inspectorDraft.Select(inspectedDevice.Anchor);
            runtimeUi.Act("工作区");
            yield return null; yield return null;
            var durationField = runtimeUi.rooms.GetComponentsInChildren<InputField>()
                .Single(field => field.name == "恢复时长（秒）");
            durationField.onEndEdit.Invoke("2.5");
            Check(inspectorDraft.Model.RecoveryDevice.Duration == 2.5f, "runtime inspector writes device duration through its input field");
            inspectorDraft.Undo();
            Check(inspectorDraft.Model.RecoveryDevice.Duration == inspectedDevice.Duration, "runtime inspector edit participates in shared undo");
            inspectorDraft.Select(inspectedDevice.Anchor);
            yield return null; yield return null;
            var fields = runtimeUi.rooms.GetComponentsInChildren<InputField>();
            fields.Single(field => field.name == "新参数名").SetTextWithoutNotify("qa-height");
            fields.Single(field => field.name == "参数值").SetTextWithoutNotify("1.75");
            runtimeUi.rooms.GetComponentsInChildren<Button>().Single(button => button.GetComponentInChildren<Text>()?.text == "设置参数")
                .onClick.Invoke();
            Check(inspectorDraft.Model.RecoveryDevice.Params.TryGetValue("qa-height", out float height) && height == 1.75f,
                "runtime inspector adds a named parameter to an empty device");
            inspectorDraft.Undo();
            inspectorDraft.Select(inspectedDevice.Anchor);
            yield return null; yield return null;
            inspectorDraft.Select(new Cell(3, 3));
            yield return null; yield return null;
            Check(runtimeUi.rooms.GetComponentsInChildren<InputField>().All(field => field.name != "恢复时长（秒）"),
                "runtime inspector refreshes when the selected device changes");
            runtimeUi.Act("返回");
            editor.StartTrial();
            yield return null; yield return null;
            var game = Object.FindObjectOfType<PuzzleHost>();
            Check(game && game.IsTrial && !game.Run.Solved, "real PuzzleHost trial starts unsolved");
            if (game.Run.PendingItem != null) game.panels.Back();
            yield return null; yield return null;
            var room = game.transform.Find(game.Run.ActiveId);
            var loadedDevice = game.Run.Room.RecoveryDevice;
            var floorCell = game.Run.Room.Room.RecoveryDeviceFloor.Value;
            var support = room.Find(loadedDevice.Id + "-ground");
            Check(support && support.localPosition == new Vector3(floorCell.X, 0, floorCell.Y),
                "runtime renders the device support on its Core floor cell");
            var subject = room.Find("Wall (3,3)");
            var control = room.Find("Wall (4,3)");
            var affected = subject.GetComponentsInChildren<Renderer>(true);
            var others = room.Cast<Transform>().Where(t => t.name.StartsWith("Wall ") && t != subject).SelectMany(t => t.GetComponentsInChildren<Renderer>(true)).ToDictionary(r => r, r => r.enabled);
            Check(affected.Length > 0 && affected.All(r => r.enabled && r.gameObject.activeInHierarchy) && control.GetComponentsInChildren<Renderer>().Any(r => r.enabled), "both bound walls visible before push");
            Check(game.Move(Vector2Int.right) && game.Run.Solved && game.Run.Player == new Vector2Int(3, 2) && game.Run.Boxes["box-a"] == new Vector2Int(4, 2), "PuzzleHost.Move E pushed box-a onto target (4,2)");
            var moving = game.Run.Snapshot();
            var movingSave = SaveCodec.Decode(SaveCodec.Encode(moving), moving.WorldId, moving.Hashes);
            Check(movingSave.Status == SaveStatus.Ok && movingSave.Data.Presentation.MotionMessage == "first push",
                "pending first-push text survives an in-flight save");
            float deadline = Time.realtimeSinceStartup + 5f;
            while (game.Busy && Time.realtimeSinceStartup < deadline) yield return null;
            Check(!game.Busy && affected.All(r => !r.enabled) && others.All(p => p.Key.enabled == p.Value), "after animation only true-wall renderers disabled; affected=" + affected.Length + ", control wall renderers=" + others.Count);
            Check(game.panels.Modal && game.panels.discoveryText.text == "first push", "first successful push runs its owner's Lua graph");
            Check(game.GetComponent<AudioSource>(), "room solved also executes a sound action");
            game.panels.Back();
            yield return null;
            Check(game.Interact() && game.panels.Modal && game.panels.discoveryText.text == "interaction",
                "F still dispatches the front box's interaction graph");
            game.panels.Back();
            yield return null;
            var before = game.Run.Player;
            Check(game.Run.Room.Room.TerrainAt(new Cell(3, 3)).CategoryId == TerrainCategories.Wall && !game.Move(Vector2Int.up) && game.Run.Player == before, "north into hidden wall refused at (3,2)");
            Check(game.Move(Vector2Int.down) && game.Run.Player == new Vector2Int(3, 1), "south into floor accepted immediately after blocked move");
            while (game.Busy) yield return null;
            Check(game.Move(Vector2Int.up), "return to the push lane");
            while (game.Busy) yield return null;
            Check(game.Move(Vector2Int.right), "second push moves the same box");
            while (game.Busy) yield return null;
            Check(!game.panels.Modal && !game.Run.Solved && affected.All(r => r.enabled), "second push does not re-fire firstPush and restores solved visibility");
            var snapshot = game.Run.Snapshot();
            var loaded = SaveCodec.Decode(SaveCodec.Encode(snapshot), snapshot.WorldId, snapshot.Hashes);
            Check(loaded.Status == SaveStatus.Ok && loaded.Data.PushedSinceRecovery[snapshot.RoomId].SequenceEqual(new[] { "box-a" }),
                "first push marker survives save round trip");
            Check(game.UseItem() && game.Run.Recovering && game.panels.Modal &&
                game.panels.discoveryText.text == "recovery start", "recovery start executes its graph through UseItem");
            game.panels.Back();
            yield return null;
            while (game.Run.Recovering) yield return null;
            var boxRenderers = room.Find("box-a").GetComponentsInChildren<Renderer>(true);
            Check(game.panels.Modal && game.panels.discoveryText.text == "recovery end" &&
                boxRenderers.Any() && boxRenderers.All(renderer => !renderer.enabled), "recovery completion executes Lua text and visibility");
            snapshot = game.Run.Snapshot();
            loaded = SaveCodec.Decode(SaveCodec.Encode(snapshot), snapshot.WorldId, snapshot.Hashes);
            Check(loaded.Status == SaveStatus.Ok && loaded.Data.PushedSinceRecovery[snapshot.RoomId].Count == 0 &&
                !loaded.Data.ObjectVisibility[snapshot.RoomId]["box-a"], "recovery clears the push marker and saves graph visibility");
            game.panels.Back();
            yield return null;
            var needed = game.Run.HomeOf("box-a") - Vector2Int.right;
            foreach (var step in StepsTo(game.Run, needed))
            {
                Check(game.Move(step), "approach restored box");
                while (game.Busy) yield return null;
            }
            Check(game.Move(Vector2Int.right), "push after recovery");
            while (game.Busy) yield return null;
            Check(game.panels.Modal && game.panels.discoveryText.text == "first push", "firstPush is armed again after completed recovery");

            game.panels.Back();
            yield return null;
            game.Leave();
            yield return null;
            var edited = editor.Panel.Draft;
            edited.Open(path, false);
            string builtinPose = edited.GetRecoveryPoseSource();
            const string customPose = "function pose(t, ctx) return {{id=ctx.boxId, x=4, y=1.25, z=2}} end";
            edited.SetRecoveryPoseSource(customPose);
            string customScript = edited.Model.RecoveryDevice.ScriptRef;
            edited.Undo();
            Check(edited.GetRecoveryPoseSource() == builtinPose && edited.AssetDefinition(customScript) == null,
                "undo restores the built-in recovery pose and removes the newly registered script");
            edited.Redo();
            var trigger = ObjectBehavior.Node(TriggerIds.RoomSolved, null, "solved");
            var recoveryStart = ObjectBehavior.Node(TriggerIds.RecoveryStart, null, "started");
            var recovery = ObjectBehavior.Node(ObjectBehavior.Lua,
                new Dictionary<string, string> { ["script"] = "self.startRecovery()" }, "recover");
            var repeat = ObjectBehavior.Node(BehaviorNodeKinds.StartRecovery, id: "repeat");
            edited.SetBehavior(new Cell(4, 3), new BehaviorGraph("solve-recovery", null,
                new[] { trigger, recovery }, new[] { new BehaviorEdge("solved", "out", "recover", "in") }));
            edited.SetRecoveryDeviceGraph(new BehaviorGraph("device-start", null,
                new[] { recoveryStart, repeat }, new[] { new BehaviorEdge("started", "out", "repeat", "in") }));
            string customPath = Path.Combine(Path.GetDirectoryName(path), "custom-recovery.room.json");
            edited.Save(customPath, false);
            edited.Open(customPath, false);
            Check(edited.GetRecoveryPoseSource() == customPose &&
                edited.RecoveryDeviceGraphs().Single().OwnerId == edited.Model.RecoveryDevice.Id &&
                edited.Room.IsRecoveryDeviceFloor(edited.Model.RecoveryDevice.Anchor),
                "save and reopen preserve the custom pose, device-owned graph and recovery floor");
            editor.StartTrial();
            yield return null; yield return null;
            var chained = Object.FindObjectOfType<PuzzleHost>();
            Check(chained && chained.IsTrial, "start a second trial with automatic recovery on solve");
            if (chained.Run.PendingItem != null) chained.panels.Back();
            yield return null;
            Check(chained.Move(Vector2Int.right) && chained.Run.Recovering && !chained.Busy &&
                chained.panels.Modal && chained.panels.discoveryText.text == "recovery start",
                "solved graph calls self.startRecovery() and dispatches immediately; start node cannot re-enter recovery");
            chained.panels.Back();
            yield return null;
            var cargo = chained.transform.Find(chained.Run.ActiveId).Find("box-a");
            float poseDeadline = Time.realtimeSinceStartup + 5;
            while (chained.Run.Recovering && Mathf.Abs(cargo.localPosition.y - 1.25f) > .01f &&
                Time.realtimeSinceStartup < poseDeadline) yield return null;
            Check(chained.Run.Recovering && Mathf.Abs(cargo.localPosition.y - 1.25f) <= .01f,
                "the production presenter applies the reloaded room script to the real box");
        }
    }
}
