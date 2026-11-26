/**
 * @file WS06RoomSolutionVerification.cs
 * @brief 单房求解的编辑器回归：用 Blueprint 构造固定房间，核对 SolveVerdict 四种结论、预算与穷尽语义、藤门乐观结论、真实步进回放与结构拒绝。
 * Run：菜单与批处理入口，逐例执行并汇总失败。
 * Build：由字符画图纸构造草稿模型，playable 为真时带恢复道具与主入口，可通过发布级校验并装配 PuzzleRun。
 * Check：调用 RoomSolution.Check，核对结论并确认求解不修改输入房间。
 * Replay：把确认有解的方向序列逐步交给 PuzzleRun.Step，核对每步被接受且结束时全部目标被占。
 * CornerDeadlock：箱子困在角落，搜索穷尽后确认无解且已展开状态数大于 0。
 * SearchExhaustion：出生区被墙隔开，单步预算耗尽报无法验证，足够预算穷尽报确认无解。
 * Window：藤门封闭时无解、抽象窗口下有解的房间只能报乐观有解。
 * Unknown：含未知节点的图使调用方传入不可模拟，结论为无法验证，发布级校验定位到该节点。
 * Blank：空白房间无法装配 ValidatedRoom，求解入口报没有可检查的起点。
 */
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Sokoban.Core;

namespace Sokoban.Editor
{
    public static class WS06RoomSolutionVerification
    {
        static readonly Vector2Int[] Moves = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        static readonly string[] Corridor = { "#########", "<.P.B.T.>", "#########" };

        static void Expect(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static RoomDraftModel Build(string[] rows, bool playable = false)
        {
            var parsed = Blueprint.Parse("qa-room", rows);
            var items = playable ? new[] { new RecoveryItem("recovery-1", "warehouse-lift", 3.7f) } : null;
            string primary = parsed.Doors.Any(d => d.Role == "entry") ? "entry" : null;
            return new RoomDraftModel(parsed.Room, parsed.Doors, primary, null, items);
        }

        static Cell Spawn(RoomDraftModel model) => model.Room.ObjectsOf(ObjectCategories.Spawn).Single().Cell;

        static RoomSolution.Result Check(RoomDraftModel model, SolveVerdict expected, int budget = 100000, bool unsimulatable = false)
        {
            string before = RoomCodec.Encode(model);
            var result = RoomSolution.Check(model, Spawn(model), budget, unsimulatable);
            Debug.Log($"WS06BFS observed={result.Verdict} expanded={result.Expanded} detail={result.Detail}");
            Expect(result.Verdict == expected, $"expected {expected}, observed {result.Verdict}");
            Expect(before == RoomCodec.Encode(model), "求解修改了输入房间");
            return result;
        }

        static void Replay()
        {
            var model = Build(Corridor, true);
            var run = PuzzleRun.FromDraft(new RoomDraft(model));
            Expect(!run.Solved, "初始布局已解开");
            var result = Check(model, SolveVerdict.Confirmed);
            Expect(result.Detail == "RRR", "最短解应为三步向右，实测 " + result.Detail);
            foreach (char move in result.Detail)
            {
                var delta = Moves["URDL".IndexOf(move)];
                var before = run.Player;
                bool moved = run.Step(delta, out string pushed);
                Debug.Log($"WS06BFS step={move} accepted={moved} player={before}->{run.Player} pushed={pushed} solved={run.Solved}");
                Expect(moved && run.Player == before + delta, "运行步进与走法不符 " + move);
            }
            Expect(run.Solved && run.Targets.All(run.Boxes.ContainsValue), "回放结束仍有未占目标");
        }

        static void CornerDeadlock()
        {
            var result = Check(Build(new[] { "######", "#B.PT#", "######" }), SolveVerdict.Unsolvable);
            Expect(result.Expanded > 0 && result.Moves.Count == 0, "角落死锁应穷尽可达状态且没有解法");
        }

        static void SearchExhaustion()
        {
            var model = Build(new[] { "#########", "#P.#B.T.#", "#########" });
            var partial = Check(model, SolveVerdict.Unverifiable, 1);
            Expect(partial.Expanded == 1, "单步预算应在第二个状态前耗尽");
            var complete = Check(model, SolveVerdict.Unsolvable);
            Expect(complete.Expanded == 2, "出生区内可达状态应各展开一次，实测共 2 个");
        }

        static void Window()
        {
            var result = Check(Build(new[] { "#########", "#P.V.B.T#", "#########" }), SolveVerdict.Optimistic);
            Expect(result.Moves.Count > 0, "乐观有解应给出窗口模型下的走法");
        }

        static void Unknown()
        {
            var model = Build(Corridor, true);
            Check(model, SolveVerdict.Confirmed);
            model.Room.Ids.Add("unknown-graph", "graph");
            model.Graphs.Add(new BehaviorGraph("unknown-graph", "box-1", new[] { new BehaviorNode("node-a", "qa.unknown") }, new BehaviorEdge[0]));
            bool unsimulatable = ObjectBehavior.AllGraphs(model).Any(g => !ObjectBehavior.CanSimulate(g));
            Expect(unsimulatable, "未知节点应被判为不可模拟");
            Check(model, SolveVerdict.Unverifiable, 100000, unsimulatable);
            var issue = RoomValidator.ValidateDraft(model, ValidationStage.Publish).Single(i => i.RuleId == "behavior.nodeUnknown");
            Expect(issue.Location.Contains("unknown-graph") && issue.Location.Contains("node-a"), "未定位未知行为 " + issue.Location);
        }

        static void Blank()
        {
            var model = new RoomDraftModel(new Room("blank", 15, 15));
            Expect(RoomSolution.CheckEntries(model, null).Contains("没有可检查的起点"), "空房未报告缺少起点");
            try { ValidatedRoom.From(model, null); throw new InvalidOperationException("空白房间通过了发布级校验"); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("spawn.missing")) { }
        }

        [MenuItem("Sokoban/验证/WS-06 单房 BFS")]
        public static void Run()
        {
            int passed = 0, total = 0;
            void Case(string name, Action action)
            {
                total++;
                try { action(); passed++; Debug.Log("WS06BFS PASS " + name); }
                catch (Exception ex) { Debug.LogError("WS06BFS FAIL " + name + " " + ex); }
            }
            Case("default-replay", Replay);
            Case("zero-budget", () => Expect(Check(Build(Corridor), SolveVerdict.Unverifiable, 0).Expanded == 0, "零预算仍展开状态"));
            Case("corner-deadlock", CornerDeadlock);
            Case("search-exhaustion", SearchExhaustion);
            Case("vine-window-optimistic", Window);
            Case("invalid-blank", Blank);
            Case("unknown-node", Unknown);
            Debug.Log($"WS06BFS RESULT {passed}/{total}");
            Expect(passed == total, "WS06 单房 BFS 回归失败");
        }
    }
}
