/**
 * @file WS06RoomSolutionVerification.cs
 * @brief Run 独立执行五种输入。Check 查验返回分类并记录搜索量；Replay 用正式步进回放每个方向。
 * Deadlock 检查内角箱子的静态判定；SearchExhaustion 用隔离出生区验证 BFS 耗尽；Unknown 在可解房间中加入带未知节点的事件图。
 */
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class WS06RoomSolutionVerification
    {
        static RoomDraft Default() => new RoomDraft(Resources.Load<TextAsset>("DefaultRoom").text);
        static void Expect(bool condition,string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
        static RoomSolution.Result Check(RoomDraft draft, Vector2Int start, RoomSolution.Outcome expected, int budget = 100000)
        {
            var input = draft.Room.DeepClone();
            var result = RoomSolution.Check(draft.Room,start,budget);
            Debug.Log($"WS06BFS observed={result.Status} expanded={result.Expanded} detail={result.Detail}");
            Expect(result.Status == expected, $"expected {expected}, observed {result.Status}");
            Expect(JToken.DeepEquals(input,draft.Room), "求解修改了输入房间");
            return result;
        }
        static Vector2Int Spawn(RoomDraft draft) => RoomFile.At(draft.Room["spawn"]);
        static void Replay()
        {
            var draft = Default();
            var run = PuzzleRun.FromDraft(draft);
            Expect(!run.Solved, "默认房已解");
            var result = Check(draft,run.Player,RoomSolution.Outcome.Solved);
            Expect(!string.IsNullOrEmpty(result.Detail), "缺少走法");
            foreach (char move in result.Detail)
            {
                Vector2Int delta;
                switch(move)
                {
                    case 'N': delta=Vector2Int.up; break;
                    case 'E': delta=Vector2Int.right; break;
                    case 'S': delta=Vector2Int.down; break;
                    case 'W': delta=Vector2Int.left; break;
                    default: throw new InvalidOperationException("无效方向 " + move);
                }
                var before=run.Player;
                bool moved=run.Step(delta,out string pushed);
                Debug.Log($"WS06BFS step={move} accepted={moved} player={before}->{run.Player} pushed={pushed} solved={run.Solved}");
                Expect(moved && run.Player==before+delta, "运行步进与走法不符");
            }
            Expect(run.Solved && run.Targets.All(run.Boxes.ContainsValue), "回放结束仍有未占目标");
        }
        static void Deadlock()
        {
            var draft=Default();
            draft.Select(RoomFile.At(draft.Find("box-a")));
            draft.Move(new Vector2Int(1,1));
            var run=PuzzleRun.FromDraft(draft);
            Expect(!run.Solved && run.Boxes.Count==1 && run.Targets.Count==1 && RoomFile.Terrain(draft.Room,new Vector2Int(0,1))=='#' && RoomFile.Terrain(draft.Room,new Vector2Int(1,0))=='#', "角落死锁前提错误");
            var result = Check(draft, run.Player, RoomSolution.Outcome.Unsolvable, 0);
            Expect(result.Expanded == 0 && result.Detail.Contains("目标格"), "内角死锁应在展开任何状态前定位到不可达目标");
        }
        static void SearchExhaustion()
        {
            var draft = Default();
            draft.SetRoomMarker(new Vector2Int(1, 5), "spawn");
            var walls = new[] { new Vector2Int(1, 4), new Vector2Int(2, 4), new Vector2Int(3, 5) };
            Expect(draft.Brush(walls, RoomTool.Wall) == null, "未能围出双格出生区");
            var run = PuzzleRun.FromDraft(draft);
            Expect(!run.Solved && run.Step(Vector2Int.right, out _) && run.Player == new Vector2Int(2, 5), "双格出生区无法步行");
            var start = Spawn(draft);
            var partial = Check(draft, start, RoomSolution.Outcome.BudgetReached, 1);
            Expect(partial.Expanded == 1, "单步预算应在第二个状态前耗尽");
            var complete = Check(draft, start, RoomSolution.Outcome.Unsolvable);
            Expect(complete.Expanded == 2, "两个可达玩家位置应各展开一次");
        }
        static void Unknown()
        {
            var draft=Default();
            ((JArray)draft.Room["events"]).Add(new JObject { ["id"]="unknown-graph", ["ownerId"]="box-a", ["nodes"]=new JArray(new JObject { ["id"]="node-a", ["kind"]="qa.unknown" }), ["edges"]=new JArray() });
            var result=Check(draft,Spawn(draft),RoomSolution.Outcome.Unsupported);
            Expect(result.Detail.Contains("box-a") && result.Detail.Contains("unknown-graph"), "未定位未知行为");
        }
        [MenuItem("Sokoban/验证/WS-06 单房 BFS")]
        public static void Run()
        {
            int passed=0, total=0;
            void Case(string name,Action action)
            {
                total++;
                try { action(); passed++; Debug.Log("WS06BFS PASS " + name); }
                catch(Exception ex) { Debug.LogError("WS06BFS FAIL " + name + " " + ex); }
            }
            Case("default-replay",Replay);
            Case("zero-budget",()=>{var draft=Default(); Expect(Check(draft,Spawn(draft),RoomSolution.Outcome.BudgetReached,0).Expanded==0,"零预算仍展开状态");});
            Case("corner-deadlock",Deadlock);
            Case("search-exhaustion",SearchExhaustion);
            Case("invalid-blank",()=>{var draft=Default(); draft.NewBlankRoom(); Expect(Check(draft,Vector2Int.zero,RoomSolution.Outcome.Invalid).Detail.Contains("出生点"),"空房未定位结构错误");});
            Case("unknown-event",Unknown);
            Debug.Log($"WS06BFS RESULT {passed}/{total}");
            Expect(passed==total,"WS06 单房 BFS 回归失败");
        }
    }
}
