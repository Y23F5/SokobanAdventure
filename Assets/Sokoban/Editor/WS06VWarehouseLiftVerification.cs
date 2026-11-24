/**
 * @file WS06VWarehouseLiftVerification.cs
 * @brief Run stages a warehouse copy for Play Mode. Observe pushes its loads, pauses mid-lift and checks rig geometry, original model scales and final cells. Shot renders the live camera. Tick records failures and exits batch mode.
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
    public static class WS06VWarehouseLiftVerification
    {
        const string Key="WS06V.Lift.Path";
        static IEnumerator routine;
        static int frame=-1;
        static void Require(bool value,string label) { if(!value) throw new InvalidOperationException(label); }
        public static void Run()
        {
            var folder=Environment.GetEnvironmentVariable("LIFT_EVIDENCE") ?? Path.GetFullPath("Logs/Lift");
            Directory.CreateDirectory(folder);
            var room=RoomFile.Parse(File.ReadAllText(Path.Combine(Application.streamingAssetsPath,"World/warehouse.room.json")));
            if(Environment.GetEnvironmentVariable("LIFT_COUNT")=="2")
            {
                ((JArray)room["boxes"]).Add(new JObject { ["id"]="box-2",["cell"]=new JArray(4,4),["recoveryCell"]=new JArray(4,4) });
                ((JArray)room["targets"]).Add(new JObject { ["id"]="target-2",["cell"]=new JArray(6,4) });
            }
            var path=Path.Combine(folder,"warehouse.room.json");
            File.WriteAllText(path,room.ToString());
            EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity");
            SessionState.SetString(Key,path); EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void Hook() => EditorApplication.update+=Tick;
        static void Tick()
        {
            string path=SessionState.GetString(Key,"");
            if(path.Length==0 || !Application.isPlaying || frame==Time.frameCount) return;
            frame=Time.frameCount;
            try { if(routine==null) routine=Observe(path); if(routine.MoveNext()) return; Debug.Log("LIFTQA PASS"); SessionState.EraseString(Key); EditorApplication.Exit(0); }
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
        static IEnumerator Observe(string path)
        {
            yield return null; yield return null;
            Time.captureFramerate=120;
            var folder=Path.GetDirectoryName(path);
            var editor=Object.FindObjectOfType<LayoutHost>(); editor.Panel.Draft.Open(path,true); editor.StartTrial();
            yield return null; yield return null;
            var host=Object.FindObjectOfType<PuzzleHost>(); host.panels.Back(); yield return null; yield return null;
            var room=host.transform.Find("warehouse"); var truck=room.Find("主题模型"); var park=truck.position;
            var boxes=host.Run.Boxes.Keys.OrderBy(id=>id,StringComparer.Ordinal).Select(id=>room.Find(id)).ToArray();
            var scales=boxes.Append(truck).SelectMany(t=>t.GetComponentsInChildren<Transform>(true)).ToDictionary(t=>t,t=>t.localScale);
            var homes=boxes.Select(t=>t.position).ToArray();
            Require(park.x>8 && !room.Find("叉车伸缩机构"),"initial exterior parking");
            foreach(char c in boxes.Length==1?"EE":"EEWNNE")
            {
                Vector2Int dir=c=='E'?Vector2Int.right:c=='W'?Vector2Int.left:Vector2Int.up;
                Require(host.Move(dir),"rejected push route "+c); while(host.Busy) yield return null;
            }
            var sources=boxes.Select(t=>t.position).ToArray();
            Require(boxes.Select((t,i)=>sources[i]==homes[i]+Vector3.right).All(ok=>ok),"push displacement");
            Debug.Log($"LIFTQA parked={park:F4} boxes={boxes.Length} scaleTransforms={scales.Count}"); Shot(folder,"00-parked");
            Require(host.UseItem(),"warehouse recovery refused");
            float[] phases={.37f,.55f,.735f,.96f}; string[] names={"insert","carry","land","retract"};
            for(int i=0;i<boxes.Length;i++) for(int j=0;j<phases.Length;j++)
            {
                float elapsed=(i+phases[j])/boxes.Length;
                while(1-host.Run.RecoveryRemaining/PuzzleRun.RecoveryDuration<elapsed) yield return null;
                var rig=room.Find("叉车伸缩机构"); var blade=rig.Find("左货叉"); var boom=rig.Find("伸缩臂"); var box=boxes[i];
                Debug.Log($"LIFTQA box={box.name} stage={names[j]} remaining={host.Run.RecoveryRemaining:F4} truck={truck.position:F4} bladeLength={blade.localScale.y:F4} boomLength={boom.localScale.y:F4} cargo={box.position:F4} scale={box.localScale:F4}");
                Require(Mathf.Abs(truck.position.x-park.x)<.001f && scales.All(p=>p.Key.localScale==p.Value),"perimeter and original proportions");
                Require(boxes.Select((b,k)=>k==i || Vector3.Distance(b.position,k<i?homes[k]:sources[k])<.001f).All(ok=>ok),"inactive load displaced");
                if(j==0) Require(blade.localScale.y>.7f && boom.localScale.y>2 && box.position==sources[i],"fork insertion");
                if(j==1) Require(box.position.y>1.45f && box.position.x<homes[i].x+1 && box.position.x>homes[i].x && blade.localScale.y>.7f,"raised transit");
                if(j==2) Require(Vector3.Distance(box.position,homes[i])<.001f,"set down");
                if(j==3) Require(blade.localScale.y<.025f && boom.localScale.y<.25f,"fork retraction");
                Shot(folder,box.name+"-"+names[j]);
                if(i==0 && j==1)
                {
                    host.panels.Pause(); float clock=host.Run.RecoveryRemaining; var pos=box.position; var fork=blade.position;
                    for(int n=0;n<30;n++) yield return null;
                    Require(host.panels.Modal && clock==host.Run.RecoveryRemaining && box.position==pos && blade.position==fork,"pause drift");
                    Debug.Log("LIFTQA pause stable 30 frames at "+clock); host.panels.Back();
                }
            }
            while(host.Run.Recovering) yield return null;
            yield return null;
            Require(truck.position==park && !room.Find("叉车伸缩机构") && scales.All(p=>p.Key.localScale==p.Value),"idle rig or scale");
            Require(boxes.Select((b,i)=>b.position==homes[i] && host.Run.Boxes[b.name]==host.Run.RecoveryCell(b.name)).All(ok=>ok),"logical and visual landing");
            Debug.Log("LIFTQA idle parked="+truck.position+" loads="+string.Join(",",boxes.Select(b=>b.name+"="+b.position))); Shot(folder,"99-idle");
        }
    }
}

