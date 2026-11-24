/**
 * @file WS06VFactoryBeltVerification.cs
 * @brief Run opens the formal factory for Play Mode. Observe pushes both loads, samples rail extension, roller count and surface velocity, carriage tracking, pause, landing and disposal. Shot renders the live camera. Tick records failures and exits batch mode.
 */
using System;
using System.IO;
using System.Linq;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sokoban.Editor
{
    public static class WS06VFactoryBeltVerification
    {
        const string Key="WS06V.Belt.Path";
        static IEnumerator routine;
        static int frame=-1;
        static void Require(bool value,string label) { if(!value) throw new InvalidOperationException(label); }
        public static void Run()
        {
            var folder=Environment.GetEnvironmentVariable("BELT_EVIDENCE") ?? Path.GetFullPath("Logs/Belt");
            Directory.CreateDirectory(folder);
            var path=Path.Combine(Application.streamingAssetsPath,"World/factory.room.json");
            SessionState.SetString("WS06V.Belt.Folder",folder);
            EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity");
            SessionState.SetString(Key,path); EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void Hook() => EditorApplication.update+=Tick;
        static void Tick()
        {
            string path=SessionState.GetString(Key,"");
            if(path.Length==0 || !Application.isPlaying || frame==Time.frameCount) return;
            frame=Time.frameCount;
            try { if(routine==null) routine=Observe(path); if(routine.MoveNext()) return; Debug.Log("BELTQA PASS"); SessionState.EraseString(Key); EditorApplication.Exit(0); }
            catch(Exception ex) { Debug.LogException(ex); SessionState.EraseString(Key); EditorApplication.Exit(1); }
        }
        static void Shot(string folder,string name)
        {
            var c=Camera.main;
            var previous=c.targetTexture; var active=RenderTexture.active;
            var rt=RenderTexture.GetTemporary(960,720,24); var image=new Texture2D(960,720,TextureFormat.RGB24,false);
            try { c.targetTexture=rt; c.Render(); RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,960,720),0,0); image.Apply(); File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG()); }
            finally { c.targetTexture=previous; RenderTexture.active=active; RenderTexture.ReleaseTemporary(rt); Object.Destroy(image); }
        }
        static Vector3 Flat(Vector3 p) => new Vector3(p.x,0,p.z);
        static IEnumerator Observe(string path)
        {
            yield return null; yield return null;
            Time.captureFramerate=120;
            string folder=SessionState.GetString("WS06V.Belt.Folder","");
            var editor=Object.FindObjectOfType<LayoutHost>(); editor.Panel.Draft.Open(path,true); editor.StartTrial();
            yield return null; yield return null;
            var host=Object.FindObjectOfType<PuzzleHost>(); if(host.Run.PendingItem!=null) host.panels.Back(); yield return null; yield return null;
            var room=host.transform.Find("factory");
            var cargo=new[]{room.Find("box-1"),room.Find("box-2")};
            var homes=cargo.Select(t=>t.position).ToArray();
            foreach(char step in "EENWNNEEE")
            {
                var direction=step=='E'?Vector2Int.right:step=='W'?Vector2Int.left:Vector2Int.up;
                Require(host.Move(direction),"route rejected at "+host.Run.Player); while(host.Busy) yield return null;
            }
            var sources=cargo.Select(t=>t.position).ToArray();
            Require(sources[0]==homes[0]+2*Vector3.right && sources[1]==homes[1]+Vector3.forward,"actual push distances");
            Shot(folder,"00-pushed"); Require(host.UseItem(),"recovery refused");
            yield return null; yield return null;
            var rigs=cargo.Select((box,i)=>room.Cast<Transform>().Single(t=>t.name=="工厂回收机构" && t.position==sources[i])).ToArray();
            var belts=rigs.Select(r=>r.Find("伸缩输送带")).ToArray();
            var arms=rigs.Select(r=>r.Find("随行机械臂")).ToArray();
            var parts=rigs.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).ToArray();
            var rollers=belts.Select(b=>b.Cast<Transform>().Where(t=>t.name=="滚筒").ToArray()).ToArray();
            float[] full={2.72f,1.72f}; int[] counts={17,11};
            for(int i=0;i<2;i++) Require(belts[i].Cast<Transform>().Count(t=>t.name=="导轨")==2 && rollers[i].Length==counts[i],"rails or roller count");
            float[] phases={.10f,.29f,.52f,.92f,.98f}; string[] labels={"extend","full","carry","land","retract"};
            for(int j=0;j<phases.Length;j++)
            {
                while(1-host.Run.RecoveryRemaining/PuzzleRun.RecoveryDuration<phases[j]) yield return null;
                var rotations=rollers.Select(row=>row.Select(t=>t.rotation).ToArray()).ToArray();
                var positions=cargo.Select(t=>t.position).ToArray();
                yield return null;
                for(int i=0;i<2;i++)
                {
                    var direction=(homes[i]-sources[i]).normalized;
                    float length=belts[i].Find("导轨").lossyScale.z;
                    Debug.Log($"BELTQA {labels[j]} {cargo[i].name} rollers={rollers[i].Length} rail={length:F4} forward={rigs[i].forward:F3} cargo={cargo[i].position:F4} arm={arms[i].position:F4}");
                    Require(Vector3.Dot(rigs[i].forward,direction)>.999f && Vector3.Distance(Flat(arms[i].position),Flat(cargo[i].position))<.001f && arms[i].position.y>cargo[i].position.y,"carriage or rig alignment");
                    if(j==0) Require(length>.02f && length<full[i]*.5f,"partial extension");
                    if(j==1 || j==2) Require(Mathf.Abs(length-full[i])<.001f,"fully extended rail");
                    if(j==2)
                    {
                        float travel=Vector3.Dot(cargo[i].position-positions[i],direction);
                        Require(travel>0 && cargo[i].position.y>.25f,"load travel direction");
                        foreach(var k in Enumerable.Range(0,rollers[i].Length))
                        {
                            var delta=rollers[i][k].rotation*Quaternion.Inverse(rotations[i][k]);
                            float surface=Vector3.Dot(delta*Vector3.up,direction)*.06f;
                            Require(surface>0 && Mathf.Abs(surface-travel)<.001f,"roller top moves against load or slips");
                        }
                        Debug.Log($"BELTQA {cargo[i].name} all roller tops forward; load travel/frame={travel:F5}");
                    }
                    if(j>=3) Require(cargo[i].position==homes[i] && length<full[i]*.5f,"landing or retraction");
                    if(j==4) Require(length<full[i]*.025f,"residual rail length");
                }
                Shot(folder,labels[j]);
                if(j==2)
                {
                    host.panels.Pause();float clock=host.Run.RecoveryRemaining;
                    var tracked=parts.Concat(cargo).ToArray();var poses=tracked.Select(t=>t.localToWorldMatrix).ToArray();
                    for(int frameIndex=0;frameIndex<30;frameIndex++) yield return null;
                    Require(host.panels.Modal && host.Run.RecoveryRemaining==clock && tracked.Select((t,i)=>t.localToWorldMatrix==poses[i]).All(ok=>ok),"pause changed time or pose");
                    Debug.Log("BELTQA paused 30 frames; frozen transforms="+tracked.Length);Shot(folder,"paused");host.panels.Back();
                }
            }
            while(host.Run.Recovering) yield return null; yield return null;
            Require(parts.All(t=>!t) && room.Find("工厂回收机构")==null && cargo.Select((t,i)=>t.position==homes[i] && host.Run.Boxes[t.name]==host.Run.RecoveryCell(t.name)).All(ok=>ok),"final disposal or landing");
            Debug.Log("BELTQA cleaned transforms="+parts.Length+" final homes="+string.Join(",",homes.Select(p=>p.ToString())));Shot(folder,"idle");
        }
    }
}
