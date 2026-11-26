/**
 * @file WS06BehaviorVerification.cs
 * @brief Run 用 Core.BehaviorGraph 构造两面内墙的夹具，绑定、保存并重开，断言身份、图的所有者、连线与实际触发结果；Tick 逐帧推进 Play 模式检查。
 * Fixture：在默认房间放两面内墙、箱子、目标并添加恢复道具，使其通过发布级校验。
 * RecoverySelection：用相同时长的两个道具验证脚本启动、存档读回与旧档选项。
 * Edges：把图内局部 ID 解析为 节点类型.端口 的语义连线。
 * Trial：用真实 PuzzleHost 推箱解开房间，比较主体墙与对照墙的渲染器，并尝试走向主体墙格。
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
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
            Check(draft.Brush(new[] { a, b }, RoomTool.Wall) == null, "constructed two interior walls");
            draft.Place(true, new Cell(3, 2), "box-a");
            draft.Place(false, new Cell(4, 2), "target-a");
            draft.Commit(new AddRecoveryItemCommand(draft.Model, new RecoveryItem("recovery-1", "warehouse-lift", RecoveryItem.DefaultDuration)));
        }

        static void RecoverySelection()
        {
            var draft = new LayoutPanel().Draft;
            var cell = new Cell(3, 3);
            Fixture(draft, cell, new Cell(4, 3));
            draft.Commit(new AddRecoveryItemCommand(draft.Model, new RecoveryItem("recovery-2", "factory-belt", RecoveryItem.DefaultDuration)));
            var run = PuzzleRun.FromDraft(draft);
            var initial = run.Snapshot();
            initial.Items.Clear();
            initial.Items.Add("recovery-1");
            initial.SelectedItem = "recovery-1";
            initial.PendingItem = null;
            run.Restore(initial);
            var begin = typeof(PuzzleRun).GetMethod("BeginScriptedRecovery", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Check((bool)begin.Invoke(run, new object[] { "recovery-2" }), "script starts the second recovery item");
            run.AdvanceRecovery(1f);
            var active = run.Snapshot();
            var saved = SaveCodec.Decode(SaveCodec.Encode(active), active.WorldId, active.Hashes);
            Check(saved.Status == SaveStatus.Ok && saved.Data.Presentation.RecoveryItemId == "recovery-2", "save records the scripted item with the same duration as the bag selection");
            var restored = PuzzleRun.FromDraft(draft);
            restored.Restore(saved.Data);
            Check(restored.Recovering && restored.ActiveItem.Id == "recovery-1" && restored.Snapshot().Presentation.RecoveryItemId == "recovery-2", "restore keeps the scripted item distinct from the bag selection");
            saved.Data.Presentation.RecoveryItemId = null;
            restored.Restore(saved.Data);
            Check(restored.Snapshot().Presentation.RecoveryItemId == "recovery-1", "legacy save uses the bag selection when durations match");
            Check(restored.AdvanceRecovery(10f) && restored.Snapshot().Presentation.RecoveryItemId == null, "completion clears the saved recovery item");
        }

        public static void Run()
        {
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

        static IEnumerator Trial(string path)
        {
            yield return null; yield return null;
            var editor = Object.FindObjectOfType<LayoutHost>();
            editor.Panel.Draft.Open(path, false);
            editor.StartTrial();
            yield return null; yield return null;
            var game = Object.FindObjectOfType<PuzzleHost>();
            Check(game && game.IsTrial && !game.Run.Solved, "real PuzzleHost trial starts unsolved");
            if (game.Run.PendingItem != null) game.panels.Back();
            yield return null; yield return null;
            var room = game.transform.Find(game.Run.ActiveId);
            var subject = room.Find("Wall (3,3)");
            var control = room.Find("Wall (4,3)");
            var affected = subject.GetComponentsInChildren<Renderer>(true);
            var others = room.Cast<Transform>().Where(t => t.name.StartsWith("Wall ") && t != subject).SelectMany(t => t.GetComponentsInChildren<Renderer>(true)).ToDictionary(r => r, r => r.enabled);
            Check(affected.Length > 0 && affected.All(r => r.enabled && r.gameObject.activeInHierarchy) && control.GetComponentsInChildren<Renderer>().Any(r => r.enabled), "both bound walls visible before push");
            Check(game.Move(Vector2Int.right) && game.Run.Solved && game.Run.Player == new Vector2Int(3, 2) && game.Run.Boxes["box-a"] == new Vector2Int(4, 2), "PuzzleHost.Move E pushed box-a onto target (4,2)");
            float deadline = Time.realtimeSinceStartup + 5f;
            while (game.Busy && Time.realtimeSinceStartup < deadline) yield return null;
            Check(!game.Busy && !game.panels.Modal && affected.All(r => !r.enabled) && others.All(p => p.Key.enabled == p.Value), "after animation only true-wall renderers disabled; affected=" + affected.Length + ", control wall renderers=" + others.Count);
            var before = game.Run.Player;
            Check(game.Run.Room.Room.TerrainAt(new Cell(3, 3)).CategoryId == TerrainCategories.Wall && !game.Move(Vector2Int.up) && game.Run.Player == before, "north into hidden wall refused at (3,2)");
            Check(game.Move(Vector2Int.down) && game.Run.Player == new Vector2Int(3, 1), "south into floor accepted immediately after blocked move");
        }
    }
}
