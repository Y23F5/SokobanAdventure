/**
 * @file WS06WorldSolutionVerification.cs
 * @brief Run exercises the published three rooms and a required-return control. Small builds and reopens a validated two-room fixture. Verify checks the budget and outcome. Replay executes all ordinary moves and independently checks the final outward step and live prerequisites.
 */
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
namespace Sokoban.Editor
{
    public static class WS06WorldSolutionVerification
    {
        static readonly Vector2Int[] Moves = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        static void Require(bool value,string detail) { if(!value) throw new InvalidOperationException(detail); }
        static RoomDraft Small(string folder)
        {
            var draft = new LayoutPanel().Draft;
            var catalog = new JObject();
            var world = WorldLayout.Empty("A");
            foreach (var id in new[]{"A","B"})
            {
                var room=(JObject)draft.Room.DeepClone();
                room["levelId"]=id;
                room["size"]=new JArray(7,5);
                room["terrainRowsNorthToSouth"]=new JArray(id=="A"?"###.###":"#######",".......","##...##","##...##","#######");
                room["spawn"]["cell"]=new JArray(2,1);
                room["boxes"][0]["cell"]=new JArray(3,1);
                room["boxes"][0]["recoveryCell"]=new JArray(3,1);
                room["targets"][0]["cell"]=new JArray(4,1);
                // A north finish uses a different door from the A-to-B connection.
                if(id=="A") ((JArray)room["doors"]).Add(new JObject{["id"]="finish",["role"]="exit",["direction"]="N",["cell"]=new JArray(3,4)});
                catalog[id]=room;
                ((JArray)world["rooms"]).Add(new JObject{["levelId"]=id,["origin"]=new JArray(id=="A"?0:7,0),["file"]="",["forwardRequiresSolved"]=id=="A"?new JArray():new JArray("A")});
            }
            world["connections"]=new JArray(new JObject{["id"]="AB",["mode"]="direct",["allowReturn"]=true,["forwardFrom"]=new JObject{["levelId"]="A",["doorId"]="exit"},["forwardTo"]=new JObject{["levelId"]="B",["doorId"]="entry"}});
            world["finish"]["endpoint"]["doorId"]="finish";
            world["finish"]["requiresSolved"]=new JArray("A","B");
            string path=Path.Combine(folder,"required-return.world.json");
            WorldLayout.Write(path,world,catalog); draft.Open(path,false); draft.UseRoom("A"); return draft;
        }
        static void Replay(RoomDraft draft,string route,bool requireReturn)
        {
            Require(route.Length>0,"empty route");
            var run=PuzzleRun.FromDraft(draft);
            int returns=0;
            for(int i=0;i<route.Length;i++)
            {
                int index="URDL".IndexOf(route[i]); Require(index>=0,"invalid direction "+route[i]);
                var move=Moves[index];
                if(i==route.Length-1)
                {
                    Require(run.CanLeaveWorld(move),"runtime refused final outward step");
                    var finish=draft.World["finish"]; var end=finish["endpoint"];
                    Require(run.ActiveId==(string)end["levelId"],"wrong finish room");
                    var door=run.Room["doors"].Single(d=>(string)d["id"]==(string)end["doorId"]);
                    Require(run.Player==run.Origin+RoomFile.At(door) && move==-RoomFile.Inward(door),"wrong finish door or outward step");
                    foreach(string id in finish["requiresSolved"].Values<string>())
                    {
                        var origin=RoomFile.Cell(draft.World["rooms"].Single(r=>(string)r["levelId"]==id)["origin"]);
                        Require(draft.Catalog[id]["targets"].All(t=>run.BoxesIn(id).Values.Contains(origin+RoomFile.At(t))),"unsolved requirement "+id);
                    }
                    Debug.Log($"WORLDQA FINAL step={i+1} {run.ActiveId}.{end["doorId"]} player={run.Player} outward={route[i]}");
                }
                else
                {
                    var previous=run.ActiveId;
                    Require(run.Step(move,out _),$"replay rejected step {i+1} {route[i]} at {previous} {run.Player}");
                    if(previous!=run.ActiveId){Debug.Log($"WORLDQA CROSS step={i+1} {previous}->{run.ActiveId}"); if(previous=="B" && run.ActiveId=="A") returns++;}
                }
            }
            Require(!requireReturn || returns>0,"missing required B to A crossing");
        }
        static void Verify(RoomDraft draft,string label,int budget,RoomSolution.Outcome expected,bool requireReturn=false)
        {
            var before=draft.Snapshot();
            var result=WorldSolution.Check(draft.World,draft.Catalog,budget);
            Debug.Log($"WORLDQA OBS {label} budget={budget} status={result.Status} expanded={result.Expanded} detail={result.Detail}");
            Require(result.Status==expected,label+" expected "+expected);
            Require(JToken.DeepEquals(before,draft.Snapshot()),"input mutated");
            if(label=="return-off") Require(result.Detail.Contains("B/entry → A/exit"),"missing disabled return direction");
            else if(expected==RoomSolution.Outcome.BudgetReached) Require(result.Expanded==budget,"incorrect budget counter");
            if(expected==RoomSolution.Outcome.Solved) Replay(draft,result.Detail,requireReturn);
        }
        [MenuItem("Sokoban/验证/WS-06 世界求解")]
        public static void Run()
        {
            var folder=Environment.GetEnvironmentVariable("WS06_WORLD_EVIDENCE")??Path.GetFullPath("Logs/WS06World"); Directory.CreateDirectory(folder);
            var campaign=new LayoutPanel().Draft;
            campaign.Open(Path.Combine(Application.streamingAssetsPath,"World/main.world.json"),false); campaign.UseRoom((string)campaign.World["start"]["levelId"]);
            var small=Small(folder);
            int passed=0,total=0;
            void Case(string name,Action action){total++;try{action();passed++;Debug.Log("WORLDQA PASS "+name);}catch(Exception ex){Debug.LogError("WORLDQA FAIL "+name+" "+ex);}}
            Case("fixture-replay",()=>Replay(small,"RUURRRRRRDDRUULLLLLLLUU",true));
            Case("budget-zero",()=>Verify(campaign,"budget-zero",0,RoomSolution.Outcome.BudgetReached));
            Case("budget-one",()=>Verify(campaign,"budget-one",1,RoomSolution.Outcome.BudgetReached));
            Case("campaign-reference",()=>{
                var run=PuzzleRun.FromDraft(campaign);
                string route="";
                foreach(string id in new[]{"warehouse","factory","forest"})
                {
                    Require(run.ActiveId==id,"unexpected reference room");
                    var exit=run.Room["doors"].Single(d=>(string)d["id"]=="exit");
                    var local=RoomSolution.Check(run.Room,run.Player-run.Origin,100000,run.World,RoomFile.At(exit));
                    Require(local.Status==RoomSolution.Outcome.Solved,"reference room search: "+local.Detail);
                    foreach(char c in local.Detail){int i="NESW".IndexOf(c);Require(run.Step(Moves[i],out _),"reference move rejected");route+="URDL"[i];}
                    var outward=-RoomFile.Inward(exit);route+="URDL"[Array.IndexOf(Moves,outward)];
                    if(id!="forest") Require(run.Step(outward,out _),"reference cross rejected");
                }
                Debug.Log("WORLDQA REFERENCE "+route);Replay(campaign,route,false);
            });
            Case("campaign",()=>Verify(campaign,"campaign",100000,RoomSolution.Outcome.Solved));
            Case("return-on",()=>Verify(small,"return-on",100000,RoomSolution.Outcome.Solved,true));
            Case("return-off",()=>{small.World["connections"][0]["allowReturn"]=false;Verify(small,"return-off",100000,RoomSolution.Outcome.BudgetReached);});
            Case("return-off-forward-finish",()=>{small.World["connections"][0]["allowReturn"]=false; small.World["finish"]["endpoint"]=new JObject{["levelId"]="B",["doorId"]="exit"}; Verify(small,"return-off-forward-finish",100000,RoomSolution.Outcome.Solved);});
            Debug.Log($"WORLDQA RESULT {passed}/{total}");Require(passed==total,"MAP06-002 failures");
        }
    }
}
