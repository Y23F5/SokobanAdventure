/**
 * @file UX06ReferenceVerification.cs
 * @brief Run builds matching object IDs in two rooms, moves the destination and imports the published world. Reject verifies draft recovery and formal refusal. Tick and Trial replay a real solve and compare renderer states across both rooms.
 */
using System;
using System.IO;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Object = UnityEngine.Object;

namespace Sokoban.Editor
{
    public static class UX06ReferenceVerification
    {
        const string Key = "UX06Reference.Path";
        const string Other = "other-room";
        static readonly Vector2Int Wall = new Vector2Int(2,0);
        static IEnumerator trial;
        static int frame = -1;
        static void Check(bool ok, string detail)
        {
            if (!ok) throw new InvalidOperationException(detail);
            Debug.Log("REFQA PASS " + detail);
        }
        static JToken ActionNode(JObject graph) => graph["nodes"].Single(n => (string)n["kind"] == "setVisible");
        static JObject Graph(RoomDraft draft) => (JObject)draft.Room["events"].Single();
        static void Reject(RoomDraft draft, string folder, string source)
        {
            var broken = (JObject)Graph(draft).DeepClone();
            ActionNode(broken)["target"]["objectId"] = "missing-object";
            draft.SetBehavior(Wall, broken);
            var path = Path.Combine(folder,"dangling.draft.json");
            File.WriteAllText(path,draft.Snapshot().ToString());
            var recovered = new LayoutPanel().Draft;
            recovered.Recover(JObject.Parse(File.ReadAllText(path)));
            Check(JToken.DeepEquals(broken,Graph(recovered)), "disk draft retains entire dangling graph");
            var rejected = Path.Combine(folder,"rejected.world.json");
            try { recovered.Save(rejected,true); throw new Exception("dangling publish succeeded"); }
            catch (InvalidDataException ex)
            {
                string location = source + "/" + broken["ownerId"] + "/" + broken["id"] + "/" + ActionNode(broken)["id"];
                Check(ex.Message.Contains(location) && ex.Message.Contains("missing-object") && !File.Exists(rejected), "publish refused: " + ex.Message);
            }
        }
        public static void Run()
        {
            string folder = Environment.GetEnvironmentVariable("UX06_REF_DIR");
            bool cross = Environment.GetEnvironmentVariable("UX06_REF_CASE") == "cross";
            Directory.CreateDirectory(folder);
            var draft = new LayoutPanel().Draft;
            string source = draft.ActiveId;
            var other = (JObject)draft.Room.DeepClone();
            other["levelId"] = Other;
            string roomPath = Path.Combine(folder,"other.room.json");
            File.WriteAllText(roomPath,other.ToString());
            draft.BeginWorld(); draft.PlaceRoom(Vector2Int.zero,false);
            draft.Attach(roomPath); draft.PlaceRoom(new Vector2Int(8,0),false);
            draft.UseRoom(source);
            draft.World["connections"] = new JArray(new JObject {
                ["id"]="link", ["mode"]="teleport", ["allowReturn"]=true,
                ["forwardFrom"]=new JObject { ["levelId"]=source, ["doorId"]="exit" },
                ["forwardTo"]=new JObject { ["levelId"]=Other, ["doorId"]="entry" }
            });
            var graph = ObjectBehavior.Template();
            var target = new JObject { ["objectId"] = "box-a" };
            if (cross) target["levelId"] = Other;
            ActionNode(graph)["target"] = target;
            draft.SetBehavior(Wall,graph);
            var bound = Graph(draft).DeepClone();
            draft.UseRoom(Other); draft.PlaceRoom(new Vector2Int(12,5),true); draft.UseRoom(source);
            Check(JToken.DeepEquals(bound,Graph(draft)), "graph unchanged after moving target room");
            var path = Path.Combine(folder,"exchange.world.json");
            draft.Save(path,true);
            var imported = new LayoutPanel().Draft;
            imported.Open(path,true); imported.UseRoom(source);
            Check(JToken.DeepEquals(bound,Graph(imported)) && RoomFile.Cell(imported.World["rooms"].Single(r => (string)r["levelId"] == Other)["origin"]) == new Vector2Int(12,5), "exchange import preserves graph and moved origin (12,5)");
            Reject(imported,folder,source);
            EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity");
            SessionState.SetString(Key,path); EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void Hook() { EditorApplication.update += Tick; }
        static void Tick()
        {
            string path = SessionState.GetString(Key,"");
            if (path.Length == 0 || !Application.isPlaying || frame == Time.frameCount) return;
            frame = Time.frameCount;
            try { if (trial == null) trial = Trial(path); if (trial.MoveNext()) return; SessionState.EraseString(Key); EditorApplication.Exit(0); }
            catch (Exception ex) { SessionState.EraseString(Key); Debug.LogException(ex); EditorApplication.Exit(1); }
        }
        static IEnumerator Trial(string path)
        {
            yield return null; yield return null;
            var editor = Object.FindObjectOfType<LayoutHost>();
            editor.Panel.Draft.Open(path,true);
            editor.Panel.Draft.UseRoom((string)editor.Panel.Draft.World["start"]["levelId"]);
            editor.StartTrial(); yield return null; yield return null;
            var game = Object.FindObjectOfType<PuzzleHost>();
            Check(game.IsTrial && !game.Run.Solved,"real host starts unsolved");
            if (game.Run.PendingItem != null) game.panels.Back(); yield return null; yield return null;
            string source = game.Run.ActiveId;
            string destination = Environment.GetEnvironmentVariable("UX06_REF_CASE") == "cross" ? Other : source;
            var subject = game.transform.Find(destination).Find("box-a");
            var target = subject.GetComponentsInChildren<Renderer>(true);
            var controls = new[]{source,Other}.SelectMany(id => game.transform.Find(id).GetComponentsInChildren<Renderer>(true)).Except(target).ToDictionary(r => r,r => r.enabled);
            if (destination == Other) Check(!subject.gameObject.activeInHierarchy, "unvisited destination root inactive before push");
            Check(target.Length > 0 && target.All(r => r.enabled) && game.transform.Find(source).Find("Wall (2, 0)").GetComponentsInChildren<Renderer>().Any(r => r.enabled), "target and graph owner renderers enabled before push");
            Check(game.Move(Vector2Int.right) && game.Run.Solved,"real push solved source room");
            double deadline = EditorApplication.timeSinceStartup + 10; while (game.Busy && EditorApplication.timeSinceStartup < deadline) yield return null;
            Check(!game.Busy && target.All(r => !r.enabled) && controls.All(p => p.Key.enabled == p.Value), $"only {destination}/box-a hidden, target renderers={target.Length}, other renderers={controls.Count}");
            if (destination == Other)
            {
                foreach (var move in new[] {Vector2Int.up,Vector2Int.right,Vector2Int.right,Vector2Int.right,Vector2Int.right})
                {
                    Check(game.Move(move), $"approach or cross {move} player={game.Run.Player} busy={game.Busy} modal={game.panels.Modal}");
                    deadline = EditorApplication.timeSinceStartup + 10; while (game.Busy && EditorApplication.timeSinceStartup < deadline) yield return null;
                }
                Check(game.Run.ActiveId == Other && subject.gameObject.activeInHierarchy && target.All(r => !r.enabled), "target room entered and active, target box still hidden");
            }
        }
    }
}


