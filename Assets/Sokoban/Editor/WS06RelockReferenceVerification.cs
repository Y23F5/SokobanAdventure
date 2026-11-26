/**
 * @file WS06RelockReferenceVerification.cs
 * @brief Run executes independent world and reference cases. Relock replays the formal solver and probes the same factory exit with two measured warehouse snapshots. Ports exercises valid branches and invalid input and output ports. Reference erases a bound target, checks export failure and reopens the undo-restored export.
 */
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
namespace Sokoban.Editor
{
    public static class WS06RelockReferenceVerification
    {
        static readonly Vector2Int[] Directions={Vector2Int.up,Vector2Int.right,Vector2Int.down,Vector2Int.left};
        static string Folder=>Environment.GetEnvironmentVariable("WS06_RELOCK_EVIDENCE")??Path.GetFullPath("Logs/RelockReference");
        static void Check(bool ok,string label){if(!ok)throw new InvalidOperationException(label);Debug.Log("GATEQA PASS "+label);}
        static RoomDraft Campaign(){var d=new LayoutPanel().Draft;d.Open(Path.Combine(Application.streamingAssetsPath,"World/main.world.json"),false);d.UseRoom("warehouse");return d;}
        static void Relock()
        {
            var draft=Campaign(); var result=WorldSolution.Check(draft.World,draft.Catalog);
            Check(result.Status==RoomSolution.Outcome.Solved,"world solver expanded="+result.Expanded+" route="+result.Detail);
            var run=PuzzleRun.FromDraft(draft);JObject atDoor=null;
            for(int i=0;i<result.Detail.Length;i++)
            {
                var delta=Directions["URDL".IndexOf(result.Detail[i])];
                if(run.ActiveId=="factory" && run.Player-run.Origin==RoomFile.At(run.Room["doors"].Single(d=>(string)d["id"]=="exit")))atDoor=run.Snapshot();
                if(i==result.Detail.Length-1)Check(run.CanLeaveWorld(delta),"replayed world route reaches finish and allows outward step");
                else if(!run.Step(delta,out _))throw new Exception("route rejected step "+(i+1));
            }
            Check(atDoor!=null,"captured factory exit before forest");
            var source=PuzzleRun.FromDraft(draft);var gate=source.Room["doors"].Single(d=>(string)d["id"]=="exit");
            Check(!source.CanApproach(gate),"warehouse initially locked");
            for(int i=0;i<3;i++)Check(source.Step(Vector2Int.right,out _),"warehouse right step "+(i+1));
            Check(source.Solved && source.CanApproach(gate),"warehouse box on target unlocks exit");
            Check(source.Step(Vector2Int.right,out var moved) && moved=="box-1" && source.EverSolved && !source.Solved && !source.CanApproach(gate),"push off target relocks warehouse, everSolved=true");
            run.Restore(atDoor);Check(run.Solved && run.Step(Vector2Int.right,out _) && run.ActiveId=="forest","solved factory with solved warehouse crosses to forest");
            var broken=(JObject)atDoor.DeepClone();broken["rooms"]["warehouse"]=source.Snapshot()["rooms"]["warehouse"].DeepClone();
            File.WriteAllText(Path.Combine(Folder,"factory-open.json"),atDoor.ToString());File.WriteAllText(Path.Combine(Folder,"factory-relocked.json"),broken.ToString());
            run.Restore(broken);var before=run.Snapshot();var exit=run.Room["doors"].Single(d=>(string)d["id"]=="exit");
            Check(run.Solved && !run.CanApproach(exit) && !run.Step(Vector2Int.right,out _) && JToken.DeepEquals(before,run.Snapshot()),"solved factory with broken warehouse refuses forest crossing and preserves snapshot");
        }
        static void Reference()
        {
            var draft=Campaign(); var graph=ObjectBehavior.Template();var node=graph["nodes"].Single(n=>(string)n["kind"]=="setVisible");
            node["target"]=new JObject{["levelId"]="factory",["objectId"]="box-1"};draft.SetBehavior(new Vector2Int(2,0),graph);
            var bound=draft.Room["events"].Single().DeepClone();draft.Save(Path.Combine(Folder,"before.world.json"),true);draft.UseRoom("factory");
            var target=draft.Find("box-1").DeepClone();var before=draft.Catalog.DeepClone();
            Check(draft.Brush(new[]{RoomFile.At(target)},RoomTool.Erase)!=null && draft.Find("box-1")!=null && JToken.DeepEquals(before,draft.Catalog),"erase over an object is refused and leaves the draft unchanged");
            draft.RemoveObject("box-1"); Check(draft.Find("box-1")==null,"remove deletes actual target box");
            Check(JToken.DeepEquals(bound,draft.Catalog["warehouse"]["events"].Single()),"source graph retains cross-room reference after erase");
            var rejected=Path.Combine(Folder,"rejected.world.json");try{draft.Save(rejected,true);throw new Exception("dangling export accepted");}
            catch(InvalidDataException ex){Check(ex.Message.Contains("factory/box-1") && ex.Message.Contains((string)node["id"]) && !File.Exists(rejected),"rejected export: "+ex.Message);}
            draft.Undo();Check(JToken.DeepEquals(target,draft.Find("box-1")) && JToken.DeepEquals(before,draft.Catalog),"single undo restores target identity, properties and source graph");
            var path=Path.Combine(Folder,"after.world.json");draft.Save(path,true);draft.Open(path,true);draft.UseRoom("factory");
            var restored=draft.Catalog["warehouse"]["events"].Single();var reference=ObjectBehavior.Effects((JObject)restored).Single()["target"];
            Check(JToken.DeepEquals(target,draft.Find((string)reference["objectId"])) && (string)reference["levelId"]==draft.ActiveId && JToken.DeepEquals(bound,restored),"reimported action resolves original factory object");
        }
        static void Ports()
        {
            var draft = Campaign();
            var graph = ObjectBehavior.Template();
            draft.SetBehavior(RoomFile.At(draft.Room["spawn"]), graph);
            var room = (JObject)draft.Room.DeepClone();
            var stored = room["events"].Last;
            var edge = stored["edges"].Last;
            RoomFile.Parse(room.ToString());
            edge["out"] = "false";
            RoomFile.Parse(room.ToString());
            void Reject(string node, string port)
            {
                try { RoomFile.Parse(room.ToString()); }
                catch (InvalidDataException ex)
                {
                    Check(ex.Message.Contains(node) && ex.Message.Contains(port), ex.Message);
                    return;
                }
                throw new Exception("无效端口通过了正式文件检查");
            }
            edge["out"] = "out";
            Reject((string)edge["from"], "out");
            edge["out"] = "true";
            edge["to"] = stored["edges"].First["from"].DeepClone();
            Reject((string)edge["to"], "in");
        }
        public static void Run(){Directory.CreateDirectory(Folder);int errors=0;foreach(Action action in new Action[]{Relock,Reference,Ports})try{action();}catch(Exception ex){errors++;Debug.LogError("GATEQA FAIL "+action.Method.Name+" "+ex);}Debug.Log("GATEQA failures="+errors);if(errors!=0)throw new Exception("verification failed");}
    }
}
