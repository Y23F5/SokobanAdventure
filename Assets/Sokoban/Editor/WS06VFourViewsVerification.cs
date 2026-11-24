/**
 * @file WS06VFourViewsVerification.cs
 * @brief Run opens the warehouse. Observe rotates the editor, trial and campaign. Sample measures mesh bounds and projected grid spacing before saving each view. Shot renders the live camera. Tick records failures and exits batch mode.
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
    public static class WS06VFourViewsVerification
    {
        const string Key="WS06V.Views.Path";
        static IEnumerator routine;
        static int frame=-1;
        static void Require(bool value,string label) { if(!value) throw new InvalidOperationException(label); }
        public static void Run()
        {
            var folder=Environment.GetEnvironmentVariable("VIEWS_EVIDENCE") ?? Path.GetFullPath("Logs/Views");
            Directory.CreateDirectory(folder);
            var path=Path.Combine(Application.streamingAssetsPath,"World/warehouse.room.json");
            SessionState.SetString("WS06V.Views.Folder",folder);
            EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity");
            SessionState.SetString(Key,path); EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void Hook() => EditorApplication.update+=Tick;
        static void Tick()
        {
            string path=SessionState.GetString(Key,"");
            if(path.Length==0 || !Application.isPlaying || frame==Time.frameCount) return;
            frame=Time.frameCount;
            try { if(routine==null) routine=Observe(path); if(routine.MoveNext()) return; Debug.Log("VIEWSQA PASS"); SessionState.EraseString(Key); EditorApplication.Exit(0); }
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
        static readonly System.Collections.Generic.Dictionary<string,Bounds> baseline = new System.Collections.Generic.Dictionary<string,Bounds>();
        static int mismatches;
        static void Sample(string stage,int i,Transform room)
        {
            var c=Camera.main; string folder=SessionState.GetString("WS06V.Views.Folder","");
            var yaw=c.transform.eulerAngles.y;
            Require(c.orthographic && Mathf.Abs(Mathf.DeltaAngle(yaw,32+90*i))<.05f && Mathf.Abs(c.transform.eulerAngles.x-55)<.05f,"unexpected camera "+yaw);
            Vector2 Pixel(Vector3 p) { var uv=c.WorldToViewportPoint(p); return new Vector2(uv.x*960,uv.y*720); }
            var origin=room.position;
            Debug.Log($"VIEWSQA SAMPLE {stage} yaw={yaw:F1} ortho={c.orthographicSize:F4} aspect={c.aspect:F4} pixelsEast={Vector2.Distance(Pixel(origin),Pixel(origin+Vector3.right)):F2} pixelsNorth={Vector2.Distance(Pixel(origin),Pixel(origin+Vector3.forward)):F2}");
            foreach(var partName in new[]{"spawn","box-1","target-1","Wall (0, 0)","Floor (4, 2)","主题模型"})
            {
                var part=room.Find(partName); var meshes=part.GetComponentsInChildren<MeshFilter>(true);
                var points=meshes.SelectMany(m=>Enumerable.Range(0,8).Select(k=>{
                    var b=m.sharedMesh.bounds; return m.transform.TransformPoint(b.center+Vector3.Scale(b.extents,new Vector3((k&1)==0?-1:1,(k&2)==0?-1:1,(k&4)==0?-1:1)));})).ToArray();
                var world=new Bounds(points[0],Vector3.zero); foreach(var p in points) world.Encapsulate(p);
                if(!baseline.TryGetValue(partName,out var reference)) baseline.Add(partName,world);
                else if(Vector3.Distance(reference.size,world.size)>.001f || Vector3.Distance(reference.center,world.center)>.001f) mismatches++;
                Debug.Log($"VIEWSQA MODEL {stage}/{i} {partName} center={world.center:F3} size={world.size:F3} active={part.gameObject.activeInHierarchy}");
            }
            if(stage=="edit")
            {
                var walls=room.Cast<Transform>().Where(p=>p.name.StartsWith("Wall ")).ToArray();
                var floor=room.Cast<Transform>().Where(p=>p.name.StartsWith("Floor ")).ToArray();
                void Frame(string label,Transform[] parts)
                {
                    var vertices=parts.SelectMany(p=>p.GetComponentsInChildren<MeshFilter>()).SelectMany(m=>m.sharedMesh.vertices.Select(v=>Pixel(m.transform.TransformPoint(v)))).ToArray();
                    float left=vertices.Min(v=>v.x), right=960-vertices.Max(v=>v.x), bottom=vertices.Min(v=>v.y), top=720-vertices.Max(v=>v.y);
                    Debug.Log($"VIEWSQA FRAME yaw={yaw:F0} {label} count={parts.Length} left={left:F2} right={right:F2} top={top:F2} bottom={bottom:F2}");
                    if(Mathf.Min(left,right,top,bottom)<0) mismatches++;
                }
                Frame("walls",walls); Frame("floor",floor);
                foreach(var id in new[]{"spawn","box-1","target-1","entry","exit","主题模型"}) Frame(id,new[]{room.Find(id)});
            }
            Shot(folder,stage+"-"+i);
        }
        static IEnumerator Observe(string path)
        {
            yield return null; yield return null; Time.captureFramerate=60;
            var editor=Object.FindObjectOfType<LayoutHost>(); editor.Panel.Draft.Open(path,true); yield return null; yield return null;
            var expected=editor.Panel.Draft.Room.DeepClone();
            var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            for(int i=0;i<4;i++)
            {
                if(i>0){var gesture=typeof(LayoutHost).GetField("gesture",flags);gesture.SetValue(editor,1);typeof(LayoutHost).GetMethod("DragCamera",flags).Invoke(editor,new object[]{new Vector3(250,0,0)});gesture.SetValue(editor,-1);}
                yield return null; Sample("edit",i,GameObject.Find("Room • warehouse").transform.Find("warehouse"));
            }
            editor.StartTrial(); yield return null; yield return null;
            for(int mode=0;mode<2;mode++)
            {
                if(mode==1){PuzzleHost.MenuRequest=PuzzleRun.Campaign(); UnityEngine.SceneManagement.SceneManager.LoadScene("Game");yield return null; yield return null; yield return null;}
                var host=Object.FindObjectOfType<PuzzleHost>(); Require(host.IsTrial==(mode==0) && JToken.DeepEquals(host.Run.Room,expected),"room or mode mismatch");
                if(host.Run.PendingItem!=null) host.panels.Back(); yield return null; yield return null;
                for(int i=0;i<4;i++) { if(i>0){host.Turn(1);while(host.Busy) yield return null;} yield return null; Sample(mode==0?"trial":"campaign",i,host.transform.Find("warehouse")); }
                if(mode==0){host.Leave();yield return null;yield return null;}
            }
            Debug.Log("VIEWSQA geometry mismatches="+mismatches); Require(mismatches==0,"geometry differs");
        }
    }
}

