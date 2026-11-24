/**
 * @file WS06BehaviorVerification.cs
 * @brief Run builds, binds, saves and reopens the two-wall fixture. Edges resolves graph-local IDs to semantic connections.
 * Tick advances Play Mode checks once per frame. Trial pushes the real host, compares wall renderers and attempts the blocked move.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;
namespace Sokoban.Editor
{
    public static class WS06BehaviorVerification
    {
        const string Key="WS06Behavior.Path";
        static IEnumerator trial;
        static int frame=-1;
        static void Check(bool ok,string detail)
        {
            if(!ok) throw new InvalidOperationException(detail);
            Debug.Log("WS06BEHAVIOR PASS " + detail);
        }
        static string[] Edges(JObject graph)
        {
            var nodes=graph["nodes"].ToDictionary(n=>(string)n["id"],n=>(string)n["kind"]);
            return graph["edges"].Select(e=>nodes[(string)e["from"]]+"."+e["out"]+"->"+nodes[(string)e["to"]]+"."+e["in"]).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
        }
        public static void Run()
        {
            var draft=new LayoutPanel().Draft;
            var a=new Vector2Int(3,3); var b=new Vector2Int(4,3);
            Check(draft.Brush(new[]{a,b},RoomTool.Wall)==null,"constructed two interior walls");
            var baseline=draft.Room.DeepClone();
            var start=RoomFile.At(draft.Room["spawn"]);
            var plain=RoomSolution.Check(draft.Room,start);
            var template=ObjectBehavior.Template(); draft.SetBehavior(a,template);
            var owner=draft.OwnerAt(a); var bound=draft.Room.DeepClone();
            Check(!string.IsNullOrEmpty(owner) && (string)draft.Room["events"].Single()["ownerId"]==owner,"first wall identity and graph binding " + owner);
            draft.Undo(); Check(draft.OwnerAt(a)==null && JToken.DeepEquals(baseline,draft.Room),"single undo removes graph and identity");
            draft.Redo(); Check(JToken.DeepEquals(bound,draft.Room),"redo restores graph and original identity");
            var original=(JObject)draft.Room["events"].Single(); var copy=ObjectBehavior.CopyFor(original);
            var expected=new[]{"condition.true->setVisible.in","roomSolved.out->condition.in"};
            Check((string)copy["id"]!=(string)original["id"] && copy["ownerId"]==null && copy["nodes"].Count()==3 && !copy["nodes"].Select(n=>(string)n["id"]).Intersect(original["nodes"].Select(n=>(string)n["id"])).Any() && Edges(copy).SequenceEqual(expected) && Edges(original).SequenceEqual(expected),"copied graph has fresh IDs and both named edges resolve inside each graph");
            copy["nodes"].Single(n=>(string)n["kind"]=="condition")["enabled"]=false;
            Check((bool)ObjectBehavior.Effects(original).Single()["visible"]==false && !ObjectBehavior.Effects(copy).Any(),"true emits hide, false emits no effect; editing copy leaves original independent");
            draft.SetBehavior(b,copy);
            Check(draft.OwnerAt(b)!=owner && (string)draft.Room["events"].Last()["ownerId"]==draft.OwnerAt(b),"copied graph bound to independent wall");
            string folder=Environment.GetEnvironmentVariable("WS06_BEHAVIOR_EVIDENCE") ?? Path.GetFullPath("Logs/WS06Behavior"); Directory.CreateDirectory(folder);
            string path=Path.Combine(folder,"two-walls.room.json"); var design=draft.Room.DeepClone(); draft.Save(path,false); draft.Open(path,false);
            Check(JToken.DeepEquals(design,JObject.Parse(File.ReadAllText(path))) && JToken.DeepEquals(design,draft.Room),"formal save and reopen preserve whole room including graph IDs, owners, edges and conditions");
            var withGraphs=RoomSolution.Check(draft.Room,start);
            Check(plain.Status==RoomSolution.Outcome.Solved && plain.Detail=="E" && withGraphs.Status==plain.Status && withGraphs.Detail==plain.Detail && withGraphs.Expanded==plain.Expanded,"BFS with and without bindings: E, expanded="+plain.Expanded);
            EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity"); SessionState.SetString(Key,path); EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void Hook(){EditorApplication.update+=Tick;}
        static void Tick()
        {
            string path=SessionState.GetString(Key,"");
            if(path.Length==0 || !Application.isPlaying || frame==Time.frameCount)return;
            frame=Time.frameCount;
            try { if(trial==null) trial=Trial(path); if(trial.MoveNext())return; SessionState.EraseString(Key); EditorApplication.Exit(0); }
            catch(Exception ex){SessionState.EraseString(Key);Debug.LogException(ex);EditorApplication.Exit(1);}
        }
        static IEnumerator Trial(string path)
        {
            yield return null; yield return null;
            var editor=Object.FindObjectOfType<LayoutHost>(); editor.Panel.Draft.Open(path,false); editor.StartTrial();
            yield return null; yield return null;
            var game=Object.FindObjectOfType<PuzzleHost>();
            Check(game && game.IsTrial && !game.Run.Solved,"real PuzzleHost trial starts unsolved");
            if(game.Run.PendingItem!=null) game.panels.Back(); yield return null; yield return null;
            var room=game.transform.Find(game.Run.ActiveId);
            var subject=room.Find("Wall (3, 3)"); var control=room.Find("Wall (4, 3)");
            var affected=subject.GetComponentsInChildren<Renderer>(true);
            var others=room.Cast<Transform>().Where(t=>t.name.StartsWith("Wall ") && t!=subject).SelectMany(t=>t.GetComponentsInChildren<Renderer>(true)).ToDictionary(r=>r,r=>r.enabled);
            Check(affected.Length>0 && affected.All(r=>r.enabled && r.gameObject.activeInHierarchy) && control.GetComponentsInChildren<Renderer>().Any(r=>r.enabled),"both bound walls visible before push");
            Check(game.Move(Vector2Int.right) && game.Run.Solved && game.Run.Player==new Vector2Int(3,2) && game.Run.Boxes["box-a"]==new Vector2Int(4,2),"PuzzleHost.Move E pushed box-a onto target (4,2)");
            for(int i=0;i<1200 && game.Busy;i++)yield return null;
            Check(!game.Busy && !game.panels.Modal && affected.All(r=>!r.enabled) && others.All(p=>p.Key.enabled==p.Value),"after animation only true-wall renderers disabled; affected="+affected.Length+", control wall renderers="+others.Count);
            var before=game.Run.Player;
            Check(RoomFile.Terrain(game.Run.Room,before+Vector2Int.up)=='#' && !game.Move(Vector2Int.up) && game.Run.Player==before,"north into hidden wall refused at (3,2)");
            Check(game.Move(Vector2Int.down) && game.Run.Player==new Vector2Int(3,1),"south into floor accepted immediately after blocked move");
        }
    }
}
