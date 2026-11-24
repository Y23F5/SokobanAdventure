/**
 * @file WS06VRecoverySessionVerification.cs
 * @brief Run stages a campaign checkpoint in an isolated profile. Observe checks stationary cargo, repeated recovery, three menu save/continue cycles and trial isolation. Tick advances the checks in real Play Mode frames.
 */
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;
namespace Sokoban.Editor
{
    public static class WS06VRecoverySessionVerification
    {
        const string Key="RecoveryQA.Active";
        static IEnumerator routine;
        static int frame=-1, failures;
        static string RoomId=>Environment.GetEnvironmentVariable("RECOVERY_ROOM");
        static string Folder=>Environment.GetEnvironmentVariable("RECOVERY_FOLDER");
        static readonly Vector2Int[] Moves={Vector2Int.up,Vector2Int.right,Vector2Int.down,Vector2Int.left};
        static void Check(bool ok,string message){Debug.Log("RECQA "+(ok?"PASS ":"FAIL ")+message);if(!ok) failures++;}
        public static void Run()
        {
            if(!Application.productName.StartsWith("RecoveryQA-") || File.Exists(CampaignSlot.PathName)) throw new InvalidOperationException("Use a fresh isolated RecoveryQA- product profile");
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity"); SessionState.SetBool(Key,true); EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void Hook()=>EditorApplication.update+=Tick;
        static void Tick()
        {
            if(!SessionState.GetBool(Key,false)||!Application.isPlaying||frame==Time.frameCount)return;
            frame=Time.frameCount;
            try { if(routine==null)routine=Observe();if(routine.MoveNext())return; }
            catch(Exception ex){Debug.LogException(ex);failures++;}
            SessionState.EraseBool(Key);Debug.Log("RECQA END failures="+failures);EditorApplication.Exit(failures==0?0:1);
        }
        static Vector3 Position(Vector2Int p)=>new Vector3(p.x,0,p.y);
        static string Rig=>RoomId=="warehouse"?"叉车伸缩机构":RoomId=="factory"?"工厂回收机构":"原木腐朽";
        static IEnumerator Observe()
        {
            yield return null;Time.captureFramerate=120;
            var seed=PuzzleRun.Campaign();
            foreach(char step in "RRRURRRRRURRRRRRLDLDDLUULURRRDLDDRRRRUURRRURUULURDRDRRDRRURRDDLUURULRDDDRR")
            {
                if(seed.ActiveId==RoomId)break;
                if(!seed.Step(Moves["URDL".IndexOf(step)],out _))throw new Exception("campaign approach blocked");
            }
            if(seed.ActiveId!=RoomId)throw new Exception("room not reached");
            seed.AcceptItem();var start=seed.Snapshot();start["player"]=RoomFile.Point(seed.Origin+RoomFile.At(seed.Room["spawn"]));seed.Restore(start);
            CampaignSlot.Save(seed); Object.FindObjectOfType<MainMenuNavigation>().ContinueGame();
            while(!Object.FindObjectOfType<PuzzleHost>())yield return null;yield return null;yield return null;
            var host=Object.FindObjectOfType<PuzzleHost>();var group=host.transform.Find(RoomId);
            bool Homes()=>host.Run.Boxes.All(b=>b.Value==host.Run.RecoveryCell(b.Key) && group.Find(b.Key).position==Position(b.Value));
            bool OtherRooms()=>((JObject)start["rooms"]).Properties().Where(p=>p.Name!=RoomId).All(p=>JToken.DeepEquals(p.Value,host.Run.Snapshot()["rooms"][p.Name]));
            for(int cycle=0;cycle<2;cycle++)
            {
                if(!host.UseItem())throw new Exception("stationary recovery refused");
                bool stationary=true;
                while(host.Run.Recovering){stationary &= Homes() && !group.Find(Rig) && !group.Find("恢复格再生") && host.Run.Boxes.Keys.All(id=>group.Find(id).GetComponentsInChildren<Renderer>().Any(r=>r.enabled));yield return null;}
                Check(stationary && Homes(),"stationary cycle "+(cycle+1)+" boxes="+host.Run.Boxes.Count);
            }
            string route=RoomId=="warehouse"?"RR":RoomId=="factory"?"RRU":"UUU";
            foreach(char step in route){if(!host.Move(Moves["URDL".IndexOf(step)]))throw new Exception("push route blocked");while(host.Busy)yield return null;}
            string cargo=host.Run.Boxes.Single(b=>b.Value!=host.Run.RecoveryCell(b.Key)).Key;
            if(!host.UseItem())throw new Exception("displaced recovery refused");
            Check(!host.UseItem(),"reentrant recovery rejected");
            foreach(float phase in new[]{.15f,.50f,.85f})
            {
                while(1-host.Run.RecoveryRemaining/PuzzleRun.RecoveryDuration<phase)yield return null;
                host.panels.Pause(); float clock=host.Run.RecoveryRemaining;var snapshot=host.Run.Snapshot();
                var parts=group.GetComponentsInChildren<Transform>(true);var poses=parts.Select(p=>p.localToWorldMatrix).ToArray();
                for(int i=0;i<30;i++)yield return null;
                Check(host.panels.Modal && clock==host.Run.RecoveryRemaining && parts.Select((p,i)=>poses[i]==p.localToWorldMatrix).All(v=>v),"pause "+phase+" remaining="+clock);
                host.panels.Back();yield return null;yield return null;yield return null;
                Check(host.Run.RecoveryRemaining<clock,"continue "+phase);host.panels.Pause(); snapshot=host.Run.Snapshot();
                var pos=group.Find(cargo).position; host.Leave();
                while(!Object.FindObjectOfType<MainMenuNavigation>())yield return null;yield return null;
                File.Copy(CampaignSlot.PathName,Path.Combine(Folder,"phase-"+Mathf.RoundToInt(phase*100)+".json"));
                Check(JToken.DeepEquals(JObject.Parse(File.ReadAllText(CampaignSlot.PathName)), JObject.Parse(snapshot.ToString())),"saved snapshot "+phase);
                Object.FindObjectOfType<MainMenuNavigation>().ContinueGame();while(!Object.FindObjectOfType<PuzzleHost>())yield return null;yield return null;yield return null;
                host=Object.FindObjectOfType<PuzzleHost>();group=host.transform.Find(RoomId);
                float dt=(float)snapshot["recoveryRemaining"]-host.Run.RecoveryRemaining;
                Check(host.Run.ActiveId==RoomId && dt>=-.0001f && dt<.08f && group.Find(Rig) && Vector3.Distance(pos,group.Find(cargo).position)<.22f && OtherRooms(),"reloaded "+phase+" elapsed="+dt+" poseDelta="+Vector3.Distance(pos,group.Find(cargo).position));
            }
            while(host.Run.Recovering)yield return null;yield return null;
            Check(Homes() && !group.Find(Rig) && OtherRooms(),"post-load landing and other rooms isolated");
            var cell=host.Run.RecoveryCell(cargo);var direction=cell-host.Run.Player;
            bool pushed=host.Move(direction);while(host.Busy)yield return null;
            Check(pushed && host.Run.Boxes[cargo]==cell+direction && group.Find(cargo).position==Position(cell+direction) && group.Find(cargo).GetComponentsInChildren<Renderer>().Any(r=>r.enabled),"first push after recovery direction="+direction+" cargo="+cargo);
            Check(host.UseItem(),"repeat displaced recovery accepted");while(host.Run.Recovering)yield return null;yield return null;Check(Homes() && !group.Find(Rig) && OtherRooms(),"second landing and cleanup");
            host.Leave();while(!Object.FindObjectOfType<MainMenuNavigation>())yield return null;yield return null;
            var bytes=File.ReadAllBytes(CampaignSlot.PathName);Object.FindObjectOfType<MainMenuNavigation>().OpenEditor();while(!Object.FindObjectOfType<LayoutHost>())yield return null;yield return null;
            var editor=Object.FindObjectOfType<LayoutHost>();editor.Panel.Draft.Open(Path.Combine(Application.streamingAssetsPath,"World",RoomId+".room.json"),true);editor.StartTrial();yield return null;yield return null;
            host=Object.FindObjectOfType<PuzzleHost>();if(host.Run.PendingItem!=null)host.panels.Back();yield return null;yield return null;
            foreach(char step in route){if(!host.Move(Moves["URDL".IndexOf(step)]))throw new Exception("trial route blocked");while(host.Busy)yield return null;}
            Check(host.IsTrial && host.UseItem(),"trial recovery started");yield return null;yield return null;host.Leave();yield return null;yield return null;
            Check(bytes.SequenceEqual(File.ReadAllBytes(CampaignSlot.PathName)) && editor.Interface.gameObject.activeInHierarchy,"trial return preserves campaign bytes");
        }
    }
}
