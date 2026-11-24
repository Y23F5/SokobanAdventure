/**
 * @file WS06FlowExchangeVerification.cs
 * @brief Run opens RuntimeEditor. Observe exchanges each authored graph, checks the live report and triggers it twice. Report reads the actual dialog text. Replay drives PuzzleHost with the displayed directions. Tick advances on Play Mode frames.
 */
using System;
using System.IO;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;
namespace Sokoban.Editor
{
    public static class WS06FlowExchangeVerification
    {
        const string Key="WS06FlowExchange.Active";
        static string Folder=>Environment.GetEnvironmentVariable("FLOW_EVIDENCE")??Path.GetFullPath("Logs/FlowExchange");
        static IEnumerator routine;
        static int frame=-1;
        static string text;
        static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);Debug.Log("FLOWQA PASS "+message);}
        public static void Run(){Directory.CreateDirectory(Folder);EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity");SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();}
        [InitializeOnLoadMethod] static void Hook()=>EditorApplication.update+=Tick;
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!Application.isPlaying||frame==Time.frameCount)return;
            frame=Time.frameCount;
            try {if(routine==null)routine=Observe();if(routine.MoveNext())return;SessionState.EraseBool(Key);EditorApplication.Exit(0);}
            catch(Exception ex){Debug.LogException(ex);SessionState.EraseBool(Key);EditorApplication.Exit(1);}
        }
        static IEnumerator Report(LayoutHost editor,string label,string expected)
        {
            editor.Interface.Act("检查");double deadline=EditorApplication.timeSinceStartup+120;
            while(editor.Panel.SolutionText.StartsWith("正在") && EditorApplication.timeSinceStartup<deadline)yield return null;
            yield return null;yield return null;
            text=editor.Interface.solutionText.text;File.WriteAllText(Path.Combine(Folder,label+"-report.txt"),text);
            Check(editor.Interface.solutionDialog.activeInHierarchy && text==editor.Panel.SolutionText && text.Contains(expected),label+" report "+expected);
            editor.Interface.Act("关闭检查");yield return null;yield return null;
        }
        static IEnumerator Replay(PuzzleHost host,string moves)
        {
            foreach(char move in moves){var directions=new[]{Vector2Int.up,Vector2Int.right,Vector2Int.down,Vector2Int.left};Check(host.Move(directions["NESW".IndexOf(move)]),"Move "+move+" player="+host.Run.Player);while(host.Busy)yield return null;}
        }
        static IEnumerator Observe()
        {
            yield return null;yield return null;Time.captureFramerate=60;
            var editor=Object.FindObjectOfType<LayoutHost>();var draft=editor.Panel.Draft;
            var cell=new Vector2Int(3,0);
            foreach(string mode in new[]{"control","expression","script"})
            {
                var author=new RoomDraft(Resources.Load<TextAsset>("DefaultRoom").text);
                author.Select(cell);author.SetSelectedName("测试墙");var graph=ObjectBehavior.Template();
                var condition=graph["nodes"].Single(n=>(string)n["kind"]=="condition");var action=(JObject)graph["nodes"].Single(n=>(string)n["kind"]=="setVisible");
                if(mode=="expression")condition["expression"]="self.id == '"+author.OwnerAt(cell)+"'";
                if(mode=="script"){action["kind"]="lua";action.Remove("visible");action["script"]="self.setVisible(false)";}
                author.SetBehavior(cell,graph);var original=author.Room.DeepClone();string first=Path.Combine(Folder,mode+"-authored.json"),last=Path.Combine(Folder,mode+"-returned.json");
                author.Save(first,true);draft.Open(first,true);draft.Save(last,true);draft.Open(last,true);
                Check(JToken.DeepEquals(original,draft.Room)&&JToken.DeepEquals(original,JObject.Parse(File.ReadAllText(last))),mode+" whole room roundtrip");
                var report=Report(editor,mode,mode=="control"?"有解":"无法验证");while(report.MoveNext())yield return null;
                if(mode!="control")Check(text.Contains(author.OwnerAt(cell)) && text.Contains((string)graph["id"]) && text.Contains((string)(mode=="script"?action:condition)["id"]),mode+" unsupported owner graph node");
                editor.StartTrial();yield return null;yield return null;var host=Object.FindObjectOfType<PuzzleHost>();if(host.Run.PendingItem!=null)host.panels.Back();yield return null;yield return null;
                var group=host.transform.Find(host.Run.ActiveId);var affected=group.Find("Wall (3, 0)").GetComponentsInChildren<Renderer>();var control=group.Find("Wall (2, 0)").GetComponentsInChildren<Renderer>();
                for(int trigger=1;trigger<=2;trigger++)
                {
                    Check(!host.Run.Solved && affected.Length>0 && affected.All(r=>r.enabled) && control.Length>0 && control.All(r=>r.enabled),mode+" visible before trigger "+trigger);
                    var replay=Replay(host,"E");while(replay.MoveNext())yield return null;
                    Check(host.Run.Solved && affected.All(r=>!r.enabled) && control.All(r=>r.enabled),mode+" trigger "+trigger+" hides only subject of two walls");
                    if(trigger==1){Check(host.UseItem(),"recover");while(host.Run.Recovering)yield return null;foreach(var r in affected)r.enabled=true;var back=Replay(host,"WS");while(back.MoveNext())yield return null;}
                }
                host.Leave();yield return null;yield return null;
            }
            draft.Open(Path.Combine(Application.streamingAssetsPath,"World/warehouse.room.json"),true);
            var solved=Report(editor,"warehouse","出生点 → exit · 有解");while(solved.MoveNext())yield return null;
            string route=text.Split('\n').Single(l=>l.StartsWith("出生点 → exit · 有解 ")).Split(' ').Last().Trim();editor.StartTrial();yield return null;yield return null;
            var game=Object.FindObjectOfType<PuzzleHost>();if(game.Run.PendingItem!=null)game.panels.Back();yield return null;yield return null;
            var replayExit=Replay(game,route);while(replayExit.MoveNext())yield return null;
            Check(game.Run.Solved && game.Run.Player==game.Run.Origin+RoomFile.At(game.Run.Room["doors"].Single(d=>(string)d["id"]=="exit")),"displayed route "+route+" solves and reaches exit");game.Leave();yield return null;yield return null;
            draft.Select(RoomFile.At(draft.Find("box-1")));draft.Move(new Vector2Int(1,1));var dead=Report(editor,"deadlock","确认无解");while(dead.MoveNext())yield return null;
            var large = new RoomDraft(Resources.Load<TextAsset>("DefaultRoom").text);
            large.NewBlankRoom("budget-room","预算检查","warehouse",new Vector2Int(14,10));
            var all = Enumerable.Range(0,14).SelectMany(x => Enumerable.Range(0,10).Select(y => new Vector2Int(x,y))).ToArray();
            large.Brush(all,RoomTool.Floor);
            large.Brush(all.Where(p => (p.x==0 || p.x==13 || p.y==0 || p.y==9) && !(p.y==4 && (p.x==0 || p.x==13))),RoomTool.Wall);
            large.SetRoomMarker(new Vector2Int(0,4),"entry");
            large.SetRoomMarker(new Vector2Int(13,4),"exit");
            large.SetRoomMarker(new Vector2Int(2,2),"spawn");
            for(int i=0;i<4;i++)
            {
                large.Place(true,new Vector2Int(3+i*2,4));
                large.Place(false,new Vector2Int(4+i*2,7));
            }
            string budgetPath=Path.Combine(Folder,"budget.room.json");
            large.Save(budgetPath,false);
            draft.Open(budgetPath,true);
            var budgetReport=Report(editor,"budget","搜索未完成");
            while(budgetReport.MoveNext()) yield return null;
            Check(text.Contains("搜索预算已用尽"),"actual page identifies exhausted default budget");
            WS06RoomSolutionVerification.Run();Debug.Log("FLOWQA COMPLETE");
        }
    }
}
