/**
 * @file UX06ReferenceVerification.cs
 * @brief 跨房与同房 setVisible 引用的真实验证：两房出现同名对象、移动目标房、发布并导入世界，再在真实试玩中比较渲染器状态。
 * Check(ok, detail)：断言失败抛 InvalidOperationException，成功写 REFQA PASS 日志。
 * ActionNode(graph)：取图中唯一的 setVisible 节点。
 * Graph(draft)：取当前房间草稿里唯一的行为图。
 * Describe(graph)：把图的 ID、所有者、节点（ID、类型、排序后的参数）与连线拼成可比较的字符串。
 * WithTarget(graph, room, object)：返回 setVisible 节点 targetObject、targetRoom 被改写的新图，room 为空时移除 targetRoom。
 * PublishIssues(draft)：对草稿全部房间执行 ObjectBehavior.Issues 与 ObjectBehavior.TargetIssues，汇总行为图目标错误。
 * Reject(draft, folder, source)：写入悬空目标图，快照落盘后用新草稿恢复并逐项比对；再要求发布前的目标校验报出“房间/所有者/图/节点 找不到目标”，且世界文件不得生成。
 * Run()：构造两房世界并设置连接，绑定行为图，移动目标房，发布后导入核对图与原点，再进入 Play Mode；环境变量 UX06_REF_DIR 给工作目录，UX06_REF_CASE=cross 选择跨房目标。
 * Hook()、Tick()：编辑器更新回调，在 Play Mode 中逐帧推进 Trial 协程，失败时记录异常并以退出码 1 结束。
 * Trial(path)：打开已发布世界并试玩，真实推箱后核对只有目标对象的渲染器被隐藏，跨房时继续走到目标房。
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sokoban.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sokoban.Editor
{
    public static class UX06ReferenceVerification
    {
        const string Key = "UX06Reference.Path";
        const string Other = "other-room";
        static readonly Cell Wall = new Cell(2, 0);
        static IEnumerator trial;
        static int frame = -1;

        static void Check(bool ok, string detail)
        {
            if (!ok) throw new InvalidOperationException(detail);
            Debug.Log("REFQA PASS " + detail);
        }

        static BehaviorNode ActionNode(BehaviorGraph graph) => graph.Nodes.Single(n => n.Kind == ObjectBehavior.SetVisible);

        static BehaviorGraph Graph(RoomDraft draft) => draft.Model.Graphs.Single();

        static string Describe(BehaviorGraph graph) =>
            graph.Id + "|" + graph.OwnerId + "|"
            + string.Join(";", graph.Nodes.Select(n => n.Id + ":" + n.Kind + ":"
                + string.Join(",", n.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value)))) + "|"
            + string.Join(";", graph.Edges.Select(e => e.FromNode + "." + e.FromPort + ">" + e.ToNode + "." + e.ToPort));

        static BehaviorGraph WithTarget(BehaviorGraph graph, string room, string target)
        {
            var action = ActionNode(graph);
            var edited = ObjectBehavior.WithParameter(ObjectBehavior.WithParameter(action, "targetObject", target), "targetRoom", room);
            return new BehaviorGraph(graph.Id, graph.OwnerId, graph.Nodes.Select(n => n.Id == action.Id ? edited : n).ToList(), graph.Edges);
        }

        static List<string> PublishIssues(RoomDraft draft)
        {
            var issues = new List<string>();
            foreach (var model in draft.Catalog.Values)
            {
                issues.AddRange(ObjectBehavior.Issues(model));
                issues.AddRange(ObjectBehavior.TargetIssues(model, id => draft.Catalog.TryGetValue(id, out var found) ? found : null));
            }
            return issues;
        }

        static void Reject(RoomDraft draft, string folder, string source)
        {
            var broken = WithTarget(Graph(draft), null, "missing-object");
            draft.SetBehavior(Wall, broken);
            var path = Path.Combine(folder, "dangling.draft.json");
            File.WriteAllText(path, draft.Snapshot());
            var recovered = new LayoutPanel().Draft;
            recovered.Recover(File.ReadAllText(path));
            Check(Describe(broken) == Describe(Graph(recovered)), "disk draft retains entire dangling graph");
            string location = source + "/" + broken.OwnerId + "/" + broken.Id + "/" + ActionNode(broken).Id;
            var issues = PublishIssues(recovered);
            var rejected = Path.Combine(folder, "rejected.world.json");
            Check(issues.Any(i => i.Contains(location) && i.Contains("missing-object")), "publish refused: " + string.Join("; ", issues));
            if (issues.Count == 0 && recovered.World != null) recovered.Save(rejected, true);
            Check(!File.Exists(rejected), "dangling publish wrote no world file");
        }

        public static void Run()
        {
            string folder = Environment.GetEnvironmentVariable("UX06_REF_DIR");
            bool cross = Environment.GetEnvironmentVariable("UX06_REF_CASE") == "cross";
            Directory.CreateDirectory(folder);
            var draft = new LayoutPanel().Draft;
            string source = draft.ActiveId;
            string roomPath = Path.Combine(folder, "other.room.json");
            File.WriteAllText(roomPath, RoomCodec.Encode(draft.Model).Replace("\"id\":\"" + source + "\"", "\"id\":\"" + Other + "\"", StringComparison.Ordinal));
            draft.BeginWorld();
            draft.PlaceRoom(new Cell(0, 0), false);
            draft.Attach(roomPath);
            draft.PlaceRoom(new Cell(8, 0), false);
            draft.UseRoom(source);
            var graph = ObjectBehavior.Template();
            graph = WithTarget(graph, cross ? Other : null, "box-a");
            draft.SetBehavior(Wall, graph);
            var bound = Graph(draft);
            var boundText = Describe(bound);
            draft.UseRoom(Other);
            draft.PlaceRoom(new Cell(12, 5), true);
            draft.UseRoom(source);
            Check(boundText == Describe(Graph(draft)), "graph unchanged after moving target room");
            var path = Path.Combine(folder, "exchange.world.json");
            draft.Save(path, true);
            var link = new WorldConnection("link", WorldCodec.Teleport, new WorldEndpoint(source, "exit"), new WorldEndpoint(Other, "entry"), true);
            var linked = WorldLayout.WithConnections(WorldCodec.Decode(File.ReadAllText(path)), new[] { link });
            File.WriteAllText(path, WorldCodec.Encode(linked));
            var imported = new LayoutPanel().Draft;
            imported.Open(path, true);
            imported.UseRoom(source);
            Check(boundText == Describe(Graph(imported)) && imported.World.Rooms.Single(r => r.LevelId == Other).Origin == new Cell(12, 5), "exchange import preserves graph and moved origin (12,5)");
            Reject(imported, folder, source);
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
            editor.Panel.Draft.Open(path, true);
            editor.Panel.Draft.UseRoom(editor.Panel.Draft.World.Start.LevelId);
            editor.StartTrial(); yield return null; yield return null;
            var game = Object.FindObjectOfType<PuzzleHost>();
            Check(game.IsTrial && !game.Run.Solved, "real host starts unsolved");
            if (game.Run.PendingItem != null) game.panels.Back(); yield return null; yield return null;
            string source = game.Run.ActiveId;
            string destination = Environment.GetEnvironmentVariable("UX06_REF_CASE") == "cross" ? Other : source;
            var subject = game.transform.Find(destination).Find("box-a");
            var target = subject.GetComponentsInChildren<Renderer>(true);
            var controls = new[] { source, Other }.SelectMany(id => game.transform.Find(id).GetComponentsInChildren<Renderer>(true)).Except(target).ToDictionary(r => r, r => r.enabled);
            if (destination == Other) Check(!subject.gameObject.activeInHierarchy, "unvisited destination root inactive before push");
            Check(target.Length > 0 && target.All(r => r.enabled) && game.transform.Find(source).Find("Wall (2,0)").GetComponentsInChildren<Renderer>().Any(r => r.enabled), "target and graph owner renderers enabled before push");
            Check(game.Move(Vector2Int.right) && game.Run.Solved, "real push solved source room");
            double deadline = EditorApplication.timeSinceStartup + 10; while (game.Busy && EditorApplication.timeSinceStartup < deadline) yield return null;
            Check(!game.Busy && target.All(r => !r.enabled) && controls.All(p => p.Key.enabled == p.Value), $"only {destination}/box-a hidden, target renderers={target.Length}, other renderers={controls.Count}");
            if (destination == Other)
            {
                foreach (var move in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.right, Vector2Int.right, Vector2Int.right })
                {
                    Check(game.Move(move), $"approach or cross {move} player={game.Run.Player} busy={game.Busy} modal={game.panels.Modal}");
                    deadline = EditorApplication.timeSinceStartup + 10; while (game.Busy && EditorApplication.timeSinceStartup < deadline) yield return null;
                }
                Check(game.Run.ActiveId == Other && subject.gameObject.activeInHierarchy && target.All(r => !r.enabled), "target room entered and active, target box still hidden");
            }
        }
    }
}
