/**
 * @file WS06FlowExchangeVerification.cs
 * @brief 在 RuntimeEditor 中验证行为图文件往返、结构检查和试玩中的重复触发。
 * Run：打开运行编辑器场景并进入 Play 模式。Hook、Tick：按帧驱动验证并记录退出状态。
 * Check：记录断言结果，失败时抛异常。
 * CheckStructure：经界面动作进入检查模式，读取并记录结构问题，再关闭检查并核对模式恢复。
 * Replay：按 U、R、D、L 方向字母驱动 PuzzleHost.Move，等待每步动画结束。
 * Author：在默认房间补上箱子、目标与恢复道具，并在边墙格绑定行为图，使房间通过发布级校验。
 * Observe：往返保存三种图并各触发两次，随后试玩正式仓库，移除箱子后检查结构错误。
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
    public static class WS06FlowExchangeVerification
    {
        const string Key = "WS06FlowExchange.Active";
        static string Folder => Environment.GetEnvironmentVariable("FLOW_EVIDENCE") ?? Path.GetFullPath("Logs/FlowExchange");
        static IEnumerator routine;
        static int frame = -1;

        static void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); Debug.Log("FLOWQA PASS " + message); }

        public static void Run() { Directory.CreateDirectory(Folder); EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity"); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); }

        [InitializeOnLoadMethod]
        static void Hook() => EditorApplication.update += Tick;

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !Application.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try { if (routine == null) routine = Observe(); if (routine.MoveNext()) return; SessionState.EraseBool(Key); EditorApplication.Exit(0); }
            catch (Exception ex) { Debug.LogException(ex); SessionState.EraseBool(Key); EditorApplication.Exit(1); }
        }

        static void CheckStructure(LayoutHost editor, string label, params string[] expectedErrors)
        {
            var previous = editor.Panel.Mode.Current;
            editor.Interface.Act("检查");
            Check(editor.Panel.Mode.Current == UiMode.Check, label + " opens structural check");
            var issues = editor.Panel.Issues;
            File.WriteAllLines(Path.Combine(Folder, label + "-report.txt"), issues.Select(i => i.RuleId + " " + i.Message));
            var errors = issues.Where(i => i.Severity == Severity.Error).Select(i => i.RuleId).ToArray();
            Check(errors.SequenceEqual(expectedErrors), label + " structural errors: " + string.Join(", ", errors));
            editor.Interface.Act("关闭检查");
            Check(editor.Panel.Mode.Current == previous, label + " closes structural check");
        }

        static IEnumerator Replay(PuzzleHost host, string moves)
        {
            var directions = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            foreach (char move in moves)
            {
                Check(host.Move(directions["URDL".IndexOf(move)]), "Move " + move + " player=" + host.Run.Player);
                while (host.Busy) yield return null;
            }
        }

        static RoomDraft Author(string mode, Cell cell, out string owner, out BehaviorGraph graph)
        {
            var author = new RoomDraft(RoomCodec.Decode(Resources.Load<TextAsset>("DefaultRoom").text));
            author.Place(true, new Cell(3, 2), "box-qa");
            author.Place(false, new Cell(4, 2), "target-qa");
            author.Commit(new AddRecoveryItemCommand(author.Model, new RecoveryItem("recovery-qa", "warehouse-lift", RecoveryItem.DefaultDuration)));
            author.SetBehavior(cell, ObjectBehavior.Template());
            owner = author.OwnerAt(cell);
            string ownerId = owner;
            var source = author.Model.Graphs.Single();
            var nodes = source.Nodes.Select(n =>
                mode == "expression" && n.Kind == ObjectBehavior.Condition ? ObjectBehavior.WithParameter(n, "expression", "self.id == '" + ownerId + "'")
                : mode == "script" && n.Kind == ObjectBehavior.SetVisible ? new BehaviorNode(n.Id, ObjectBehavior.Lua, n.Cell, new Dictionary<string, string> { ["script"] = "self.setVisible(false)" })
                : n).ToList();
            author.SetBehavior(cell, new BehaviorGraph(source.Id, owner, nodes, source.Edges));
            graph = author.Model.Graphs.Single();
            return author;
        }

        static IEnumerator Observe()
        {
            yield return null; yield return null;
            Time.captureFramerate = 60;
            var editor = Object.FindObjectOfType<LayoutHost>();
            var draft = editor.Panel.Draft;
            var cell = new Cell(3, 0);
            foreach (string mode in new[] { "control", "expression", "script" })
            {
                var author = Author(mode, cell, out string owner, out BehaviorGraph graph);
                string original = RoomCodec.Encode(author.Model);
                string first = Path.Combine(Folder, mode + "-authored.json"), last = Path.Combine(Folder, mode + "-returned.json");
                author.Save(first, true);
                draft.Open(first, true);
                draft.Save(last, true);
                draft.Open(last, true);
                Check(original == File.ReadAllText(first) && original == File.ReadAllText(last) && original == RoomCodec.Encode(draft.Model), mode + " whole room roundtrip");
                var returned = draft.Model.Graphs.Single();
                Check(returned.OwnerId == owner && returned.Id == graph.Id, mode + " returned graph retains its owner and ID");
                CheckStructure(editor, mode);
                editor.StartTrial(); yield return null; yield return null;
                var host = Object.FindObjectOfType<PuzzleHost>();
                if (host.Run.PendingItem != null) host.panels.Back();
                yield return null; yield return null;
                var group = host.transform.Find(host.Run.ActiveId);
                var affected = group.Find("Wall (3,0)").GetComponentsInChildren<Renderer>();
                var control = group.Find("Wall (2,0)").GetComponentsInChildren<Renderer>();
                for (int trigger = 1; trigger <= 2; trigger++)
                {
                    Check(!host.Run.Solved && affected.Length > 0 && affected.All(r => r.enabled) && control.Length > 0 && control.All(r => r.enabled), mode + " visible before trigger " + trigger);
                    var replay = Replay(host, trigger == 1 ? "R" : "LDR");
                    while (replay.MoveNext()) yield return null;
                    Check(host.Run.Solved && affected.All(r => !r.enabled) && control.All(r => r.enabled), mode + " trigger " + trigger + " hides only subject of two walls");
                    if (trigger == 1)
                    {
                        Check(host.UseItem(), "recover");
                        while (host.Run.Recovering) yield return null;
                        foreach (var r in affected) r.enabled = true;
                        yield return null;
                    }
                }
                host.Leave(); yield return null; yield return null;
            }

            draft.Open(Path.Combine(Application.streamingAssetsPath, "World/warehouse.room.json"), true);
            CheckStructure(editor, "warehouse");
            editor.StartTrial(); yield return null; yield return null;
            var game = Object.FindObjectOfType<PuzzleHost>();
            if (game.Run.PendingItem != null) game.panels.Back();
            yield return null; yield return null;
            var replayExit = Replay(game, WS06RelockReferenceVerification.WarehouseRouteToExit);
            while (replayExit.MoveNext()) yield return null;
            Check(game.Run.Solved && game.Run.PlayerLocal == game.Run.Room.Doors.Single(d => d.Role == "exit").Cell, "warehouse walkthrough solves the room and reaches the exit");
            game.Leave(); yield return null; yield return null;

            draft.RemoveObject(draft.Room.ObjectsOf(ObjectCategories.Box).Single().Id);
            CheckStructure(editor, "missing-box", "target.exceedsBoxes");
            Debug.Log("FLOWQA COMPLETE");
        }
    }
}
