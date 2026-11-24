/**
 * @file UX06PassageVerification.cs
 * @brief Run 创建隔墙房间并保存重读，Solver 核对预算、未知节点及出口路线。Tick 按帧驱动真实试玩；
 * Trial 先检查关闭碰撞，再推箱解开并移出目标，核对永久开放、运行快照与仅表现墙的对照。
 */
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sokoban.Editor
{
    public static class UX06PassageVerification
    {
        const string SessionKey = "Sokoban.QA.UX06Passage";
        static readonly Vector2Int Gate = new Vector2Int(2, 3);
        static readonly Vector2Int Exit = new Vector2Int(3, 6);
        static readonly string[] Cases = { "open", "false", "visual", "lua-true", "lua-false" };
        static IEnumerator steps;
        static int lastFrame = -1;

        static void Require(bool ok, string reason)
        {
            if (!ok) throw new InvalidOperationException(reason);
        }

        static RoomDraft Fixture(string kind, bool enabled)
        {
            var room = JObject.Parse(Resources.Load<TextAsset>("DefaultRoom").text);
            room["terrainRowsNorthToSouth"] = new JArray("###.###", "#.....#", "#.....#", "#######", "......#", "#.....#", "#######");
            room["doors"][0]["cell"] = new JArray(0, 2);
            room["doors"][1]["cell"] = RoomFile.Point(Exit);
            room["doors"][1]["direction"] = "N";
            var draft = new RoomDraft(room.ToString());
            var graph = ObjectBehavior.Template();
            graph["nodes"].Single(n => (string)n["kind"] == "condition")["enabled"] = enabled;
            var action = (JObject)graph["nodes"].Single(n => (string)n["kind"] == "setVisible");
            if (kind != "setVisible") { action.Remove("visible"); action["kind"] = kind; }
            draft.SetBehavior(Gate, graph);
            return draft;
        }

        static void Solver(RoomDraft draft, bool opens)
        {
            var start = RoomFile.At(draft.Room["spawn"]);
            var result = RoomSolution.Check(draft.Room, start, destination: Exit);
            Debug.Log($"UX06 BFS opens={opens} status={result.Status} expanded={result.Expanded} detail={result.Detail}");
            if (!opens)
            {
                Require(result.Status == RoomSolution.Outcome.Unsolvable, "封闭隔墙仍有通向指定出口的路线");
                return;
            }
            Require(result.Status == RoomSolution.Outcome.Solved, "可开放的隔墙未找到解");
            var run = PuzzleRun.FromDraft(draft);
            bool crossed = false;
            foreach (char c in result.Detail)
            {
                int index = "NESW".IndexOf(c);
                Require(index >= 0, "未知 BFS 方向 " + c);
                var delta = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left }[index];
                bool previouslySolved = run.EverSolved;
                Require(run.Step(delta, out _), "BFS 回放被挡 " + run.Player + " " + c);
                if (run.Player == Gate) { Require(previouslySolved, "解开前穿墙"); crossed = true; }
            }
            Require(crossed && run.Player == Exit && run.Solved, "BFS 回放未穿过隔墙并以解开状态抵达出口");
            var zero = RoomSolution.Check(draft.Room, start, 0, destination: Exit);
            Require(zero.Status == RoomSolution.Outcome.BudgetReached && zero.Expanded == 0, "零预算分类或展开计数错误");
        }

        public static void Run()
        {
            string folder = Path.GetFullPath(Environment.GetEnvironmentVariable("UX06_PASSAGE_EVIDENCE") ?? "Logs/UX06Passage");
            Directory.CreateDirectory(folder);
            foreach (string name in Cases)
            {
                var draft = Fixture(name == "visual" ? "setVisible" : "openPassage", name != "false");
                bool lua = name.StartsWith("lua-", StringComparison.Ordinal);
                if (lua)
                {
                    var graph = (JObject)draft.Room["events"].Single().DeepClone();
                    var condition = graph["nodes"].Single(n => (string)n["kind"] == "condition");
                    string owner = draft.OwnerAt(Gate);
                    condition["expression"] = "self.id " + (name == "lua-true" ? "==" : "~=") + " '" + owner + "'";
                    draft.SetBehavior(Gate, graph);
                }
                var before = draft.Room.DeepClone();
                string path = Path.Combine(folder, name + ".room.json");
                draft.Save(path, false); draft.Open(path, false);
                Require(JToken.DeepEquals(before, draft.Room) && RoomFile.Terrain(draft.Room, Gate) == '#', "正式文件重读改变设计 " + name);
                if (lua)
                {
                    var luaResult = RoomSolution.Check(draft.Room, RoomFile.At(draft.Room["spawn"]), destination: Exit);
                    Require(luaResult.Status == RoomSolution.Outcome.Unsupported && luaResult.Detail.Contains(draft.OwnerAt(Gate)), "Lua 条件未报告无法验证");
                    Debug.Log("UX06 BFS " + name + " " + luaResult.Status + " " + luaResult.Detail);
                }
                else Solver(draft, name == "open");
            }
            var unknown = Fixture("qa.unknown", true);
            var result = RoomSolution.Check(unknown.Room, RoomFile.At(unknown.Room["spawn"]), destination: Exit);
            var node = unknown.Room["events"].Single()["nodes"].Single(n => (string)n["kind"] == "qa.unknown");
            Require(result.Status == RoomSolution.Outcome.Unsupported && result.Detail.Contains(unknown.OwnerAt(Gate)) && result.Detail.Contains((string)node["id"]), "未知节点未定位 " + result.Detail);
            Debug.Log("UX06 unsupported " + result.Detail);
            EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity");
            SessionState.SetString(SessionKey, folder);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod] static void Hook() { EditorApplication.update += Tick; }
        static void Tick()
        {
            string folder = SessionState.GetString(SessionKey, "");
            if (folder.Length == 0 || !Application.isPlaying || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            try
            {
                if (steps == null) steps = Trial(folder);
                if (steps.MoveNext()) return;
                SessionState.EraseString(SessionKey); Debug.Log("UX06 PASS all"); EditorApplication.Exit(0);
            }
            catch (Exception ex) { SessionState.EraseString(SessionKey); Debug.LogException(ex); EditorApplication.Exit(1); }
        }

        static IEnumerator Trial(string folder)
        {
            yield return null; yield return null;
            var editor = Object.FindObjectOfType<LayoutHost>();
            foreach (string name in Cases)
            {
                editor.Panel.Draft.Open(Path.Combine(folder, name + ".room.json"), false);
                editor.StartTrial(); yield return null; yield return null;
                var host = Object.FindObjectOfType<PuzzleHost>();
                Require(host && host.IsTrial, "未进入真实试玩");
                if (host.Run.PendingItem != null) host.panels.Back();
                yield return null; yield return null;
                var start = host.Run.Player;
                Require(start + Vector2Int.up == Gate && !host.Busy && !host.panels.Modal && !host.Move(Vector2Int.up) && host.Run.Player == start, "初始墙未阻挡");
                foreach (var move in new[]{Vector2Int.right, Vector2Int.right, Vector2Int.left, Vector2Int.left})
                {
                    Require(host.Move(move), "推箱或回到墙前失败 " + host.Run.Player);
                    for (double deadline = Time.realtimeSinceStartupAsDouble + 5; host.Busy && Time.realtimeSinceStartupAsDouble < deadline;) yield return null;
                    Require(!host.Busy, $"等待移动超时 {name} player={host.Run.Player} modal={host.panels.Modal} motion={host.Run.Motion}");
                    if (move == Vector2Int.right)
                        Require(host.Run.Solved == (host.Run.Player.x == 3), "第一次推箱应解开，第二次应移出目标");
                }
                bool opens = name == "open" || name == "lua-true";
                Require(host.Run.EverSolved && !host.Run.Solved && host.Run.Player == start, "未形成曾解开、当前未解的状态");
                if (name == "visual")
                {
                    var wall = host.transform.Find(host.Run.ActiveId).Find("Wall " + Gate);
                    var meshes = wall.GetComponentsInChildren<Renderer>(true);
                    Require(meshes.Length > 0 && meshes.All(mesh => !mesh.enabled), "setVisible 墙未隐藏");
                }
                string save = Path.Combine(folder, name + ".session.json");
                File.WriteAllText(save, host.Run.Snapshot().ToString());
                var restored = PuzzleRun.FromDraft(editor.Panel.Draft);
                var disk = JObject.Parse(File.ReadAllText(save));
                var record = disk["rooms"][host.Run.ActiveId];
                var cells = record["openPassages"];
                if (name == "lua-true")
                    Require(cells is JArray && cells.Count() == 1 && RoomFile.Cell(cells[0]) == Gate, "Lua 真条件快照缺少唯一开放墙");
                if (name == "lua-false")
                    Require(cells == null || cells is JArray && !cells.Any(), "Lua 假条件记录了开放墙");
                restored.Restore(disk);
                Require(!restored.Solved && restored.Player == start && restored.Step(Vector2Int.up,out _) == opens && restored.Player == (opens ? Gate : start), "存读后通行状态错误 " + name);
                if (name.StartsWith("lua-", StringComparison.Ordinal))
                {
                    if (opens)
                    {
                        Require(restored.Step(Vector2Int.up,out _) && restored.Player == Gate + Vector2Int.up, "读档后未穿至墙另一侧");
                        Require(restored.Step(Vector2Int.down,out _) && restored.Step(Vector2Int.down,out _) && restored.Player == start, "读档后无法原路返回");
                    }
                    else Require(restored.Step(Vector2Int.down,out _) && restored.Step(Vector2Int.up,out _) && restored.Player == start && !restored.Step(Vector2Int.up,out _), "读档后普通地板或关墙回放错误");
                    Debug.Log("UX06 REPLAY " + name + " openPassages=" + (cells?.ToString(Newtonsoft.Json.Formatting.None) ?? "absent") + " final=" + restored.Player);
                }
                Require(!host.panels.Modal && host.Move(Vector2Int.up) == opens && host.Run.Player == (opens ? Gate : start), "真实试玩通行状态错误 " + name);
                if (opens)
                {
                    for (double deadline = Time.realtimeSinceStartupAsDouble + 5; host.Busy && Time.realtimeSinceStartupAsDouble < deadline;) yield return null;
                    Require(host.Move(Vector2Int.up) && host.Run.Player == Gate + Vector2Int.up, "未能穿至墙另一侧");
                }
                Debug.Log("UX06 PASS runtime and restored collision " + name);
                host.Leave(); yield return null; yield return null;
            }
        }
    }
}
