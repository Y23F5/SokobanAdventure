/**
 * @file WS06FlowExchangeVerification.cs
 * @brief Run opens RuntimeEditor. Observe exchanges each authored Core.BehaviorGraph through save and reopen, checks the live report and triggers it twice. Report reads the actual dialog text. Replay drives PuzzleHost with the solver direction letters. Tick advances on Play Mode frames.
 * Author：在默认房间补上箱子、目标与恢复道具，并在边墙格绑定行为图，使房间通过发布级校验。
 * Report：点击检查页并读取真实对话框文字，结论用 确认有解、乐观有解、确认无解、无法验证 四种。
 * Replay：按 U、R、D、L 方向字母驱动 PuzzleHost.Move。
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
        static string text;

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

        static IEnumerator Report(LayoutHost editor, string label, string expected)
        {
            editor.Interface.Act("检查");
            double deadline = EditorApplication.timeSinceStartup + 120;
            while (editor.Panel.SolutionText.StartsWith("正在") && EditorApplication.timeSinceStartup < deadline) yield return null;
            yield return null; yield return null;
            text = editor.Interface.solutionText.text;
            File.WriteAllText(Path.Combine(Folder, label + "-report.txt"), text);
            Check(editor.Interface.solutionDialog.activeInHierarchy && text == editor.Panel.SolutionText && text.Contains(expected), label + " report " + expected);
            editor.Interface.Act("关闭检查");
            yield return null; yield return null;
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
                var report = Report(editor, mode, mode == "control" ? "确认有解" : "无法验证");
                while (report.MoveNext()) yield return null;
                if (mode != "control")
                {
                    var culprit = ObjectBehavior.AllGraphs(draft.Model).Single(g => !ObjectBehavior.CanSimulate(g) || g.Nodes.Any(n => !BehaviorNodeKinds.CanSimulate(n)));
                    Check(culprit.OwnerId == owner && culprit.Id == graph.Id && culprit.Nodes.Any(n => !BehaviorNodeKinds.CanSimulate(n)), mode + " unsupported owner graph node located by graph model");
                }
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
                    var plan = RoomSolution.Check(host.Run.Room.Room, host.Run.PlayerLocal);
                    Check(plan.Verdict == SolveVerdict.Confirmed && plan.Detail.Length > 0, mode + " solver route before trigger " + trigger + " " + plan.Detail);
                    var replay = Replay(host, plan.Detail);
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
            var solved = Report(editor, "warehouse", "出生点 · 确认有解");
            while (solved.MoveNext()) yield return null;
            string route = text.Split('\n').Single(l => l.StartsWith("出生点 · 确认有解 ")).Split(' ').Last().Trim();
            editor.StartTrial(); yield return null; yield return null;
            var game = Object.FindObjectOfType<PuzzleHost>();
            if (game.Run.PendingItem != null) game.panels.Back();
            yield return null; yield return null;
            var replayExit = Replay(game, route);
            while (replayExit.MoveNext()) yield return null;
            Check(game.Run.Solved, "displayed route " + route + " solves the room");
            game.Leave(); yield return null; yield return null;

            draft.Select(draft.Room.ObjectsOf(ObjectCategories.Box).Single().Cell);
            draft.Move(new Cell(1, 1));
            var dead = Report(editor, "deadlock", "确认无解");
            while (dead.MoveNext()) yield return null;

            var large = new RoomDraft(RoomCodec.Decode(Resources.Load<TextAsset>("DefaultRoom").text));
            large.NewBlankRoom("budget-room", "预算检查", "warehouse", new Cell(14, 10));
            var all = Enumerable.Range(0, 14).SelectMany(x => Enumerable.Range(0, 10).Select(y => new Cell(x, y))).ToArray();
            large.Brush(all, RoomTool.Floor);
            large.Brush(all.Where(p => (p.X == 0 || p.X == 13 || p.Y == 0 || p.Y == 9) && !(p.Y == 4 && (p.X == 0 || p.X == 13))), RoomTool.Wall);
            large.SetRoomMarker(new Cell(0, 4), "entry");
            large.SetRoomMarker(new Cell(13, 4), "exit");
            large.SetRoomMarker(new Cell(1, 4), "spawn");
            for (int i = 0; i < 4; i++)
            {
                large.Place(true, new Cell(3 + i * 2, 4));
                large.Place(false, new Cell(4 + i * 2, 7));
            }
            large.Commit(new AddRecoveryItemCommand(large.Model, new RecoveryItem("recovery-budget", "warehouse-lift", RecoveryItem.DefaultDuration)));
            string budgetPath = Path.Combine(Folder, "budget.room.json");
            large.Save(budgetPath, false);
            draft.Open(budgetPath, true);
            var budgetReport = Report(editor, "budget", "无法验证");
            while (budgetReport.MoveNext()) yield return null;
            WS06RoomSolutionVerification.Run();
            Debug.Log("FLOWQA COMPLETE");
        }
    }
}
