/**
 * @file WS06VForestRecoveryVerification.cs
 * @brief Run opens the formal forest for Play Mode. Observe pushes one log and checks fragment poses, pause, regrowth and restored meshes. Pixels compares paired camera renders with the sampled meshes temporarily hidden. Shot renders the live camera. Tick records failures and exits batch mode.
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
    public static class WS06VForestRecoveryVerification
    {
        const string Key="WS06V.Forest.Path";
        static IEnumerator routine;
        static int frame=-1;
        static void Require(bool value,string label) { if(!value) throw new InvalidOperationException(label); }
        public static void Run()
        {
            var folder=Environment.GetEnvironmentVariable("FOREST_EVIDENCE") ?? Path.GetFullPath("Logs/Forest");
            Directory.CreateDirectory(folder);
            var path=Path.Combine(Application.streamingAssetsPath,"World/forest.room.json");
            SessionState.SetString("WS06V.Forest.Folder",folder);
            EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity");
            SessionState.SetString(Key,path); EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] static void Hook() => EditorApplication.update+=Tick;
        static void Tick()
        {
            string path=SessionState.GetString(Key,"");
            if(path.Length==0 || !Application.isPlaying || frame==Time.frameCount) return;
            frame=Time.frameCount;
            try { if(routine==null) routine=Observe(path); if(routine.MoveNext()) return; Debug.Log("FORESTQA PASS"); SessionState.EraseString(Key); EditorApplication.Exit(0); }
            catch(Exception ex) { Debug.LogException(ex); SessionState.EraseString(Key); EditorApplication.Exit(1); }
        }
        static Color32[] Shot(string folder,string name)
        {
            var c=Camera.main;
            var previous=c.targetTexture; var active=RenderTexture.active;
            var rt=RenderTexture.GetTemporary(960,720,24); var image=new Texture2D(960,720,TextureFormat.RGB24,false);
            try { c.targetTexture=rt; c.Render(); RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,960,720),0,0); image.Apply(); if(name!=null) File.WriteAllBytes(Path.Combine(folder,name+".png"),image.EncodeToPNG()); return image.GetPixels32(); }
            finally { c.targetTexture=previous; RenderTexture.active=active; RenderTexture.ReleaseTemporary(rt); Object.Destroy(image); }
        }
        static int Pixels(string folder,string label,Renderer[] meshes)
        {
            var before=Shot(folder,label);
            var enabled=meshes.Where(r=>r.enabled).ToArray();
            Color32[] after;
            try { foreach(var r in enabled) r.enabled=false; after=Shot(folder,label+"-without"); }
            finally { foreach(var r in enabled) r.enabled=true; }
            int count=0;
            for(int i=0;i<before.Length;i++)
            {
                var a=before[i]; var b=after[i];
                if(Math.Max(Math.Abs(a.r-b.r),Math.Max(Math.Abs(a.b-b.b),Math.Abs(a.g-b.g)))>8) count++;
            }
            Debug.Log($"FORESTQA pixels {label}={count} enabled={enabled.Length}"); return count;
        }
        static void LogBounds(string folder,string label,MeshRenderer[] meshes,Transform floor,Transform anchor)
        {
            var candidates=meshes.Where(r=>r.enabled && r.gameObject.activeInHierarchy && Nonzero(r.transform.lossyScale)).ToArray();
            var top=floor.GetComponentsInChildren<MeshRenderer>().Max(r=>r.bounds.max.y);
            var baseline=Shot(folder,null);
            Bounds all=default, visible=default;
            int seen=0;
            for(int i=0;i<candidates.Length;i++)
            {
                var mesh=candidates[i]; var b=mesh.bounds;
                if(i==0) all=b; else all.Encapsulate(b);
                Color32[] off;
                try { mesh.enabled=false; off=Shot(folder,null); } finally { mesh.enabled=true; }
                bool contributes=false;
                for(int k=0;k<off.Length && !contributes;k++)
                    contributes=Math.Abs(off[k].r-baseline[k].r)>8 || Math.Abs(off[k].g-baseline[k].g)>8 || Math.Abs(off[k].b-baseline[k].b)>8;
                if(contributes) { if(seen++==0) visible=b; else visible.Encapsulate(b); }
                Debug.Log($"FORESTQA MESH {label} {mesh.name} contributes={contributes} min={b.min:F6} max={b.max:F6}");
            }
            Debug.Log($"FORESTQA BOUNDS {label} anchor={anchor.position:F5} floor={floor.name} top={top:F6} nonzero={candidates.Length} allMin={all.min:F6} allMax={all.max:F6} contributing={seen} visibleMin={visible.min:F6} visibleMax={visible.max:F6}");
        }
        static bool Nonzero(Vector3 scale) => Mathf.Abs(scale.x)>1e-6f && Mathf.Abs(scale.y)>1e-6f && Mathf.Abs(scale.z)>1e-6f;
        static int MeasureOcclusion(string folder,string label,Renderer[] subject,Transform player)
        {
            int normal=Pixels(folder,label,subject);
            var renderers=player.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).ToArray();
            try
            {
                foreach(var renderer in renderers) renderer.enabled=false;
                int withoutPlayer=Pixels(folder,label+"-no-player",subject);
                Debug.Log($"FORESTQA occlusion {label} normal={normal} withoutPlayer={withoutPlayer} playerPosition={player.position:F3} playerScale={player.lossyScale:F5} enabledPlayerMeshes={renderers.Length}");
            }
            finally { foreach(var renderer in renderers) renderer.enabled=true; }
            return normal;
        }
        static IEnumerator Observe(string path)
        {
            yield return null; yield return null; Time.captureFramerate=120;
            string folder=SessionState.GetString("WS06V.Forest.Folder","");
            var editor=Object.FindObjectOfType<LayoutHost>(); editor.Panel.Draft.Open(path,true); editor.StartTrial(); yield return null; yield return null;
            var host=Object.FindObjectOfType<PuzzleHost>(); if(host.Run.PendingItem!=null) host.panels.Back(); yield return null; yield return null;
            var room=host.transform.Find("forest"); var box=room.Find("box-1"); var home=box.position; var player=room.Find((string)host.Run.Room["spawn"]["id"]);
            var cargo=box.GetComponentsInChildren<Renderer>(true); var originals=cargo.Select(r=>r.enabled).ToArray();
            for(int i=0;i<3;i++){ Require(host.Move(Vector2Int.up),"north move rejected"); while(host.Busy) yield return null; }
            Require(box.position==home+Vector3.forward && host.Run.Boxes[box.name]==new Vector2Int(2,6),"push to (2,6)");
            Require(Pixels(folder,"01-pushed",cargo)>0,"pushed log invisible"); Require(host.UseItem(),"recovery rejected");
            var clip=Resources.LoadAll<AnimationClip>("Diorama/ForestDecay").First(c=>!c.name.StartsWith("__preview__"));
            Debug.Log($"FORESTQA clip={clip.name} fps={clip.frameRate} length={clip.length} animatedPaths={AnimationUtility.GetCurveBindings(clip).Select(b=>b.path).Distinct().Count()}");
            Transform[] pieces=null; Matrix4x4[] early=null; Transform tree=null; float height=0; Transform fragments=null; int errors=0;
            void Test(bool ok,string detail){Debug.Log("FORESTQA "+(ok?"PASS ":"FAIL ")+detail);if(!ok)errors++;}
            foreach(float phase in new[]{.13f,.43f,.76f,.95f})
            {
                while(1-host.Run.RecoveryRemaining/PuzzleRun.RecoveryDuration<phase) yield return null;
                var vine = room.Find("恢复藤蔓/Vine0");
                Test(vine && (phase < .2f ? vine.localScale.x > .9f : phase < .5f ? vine.localScale.x < .01f : vine.localScale.x > .01f),"vine motion at " + phase);
                fragments=room.Find("原木腐朽"); tree=room.Find("恢复格再生");
                var meshes=fragments.GetComponentsInChildren<MeshRenderer>(true);
                Test(cargo.All(r=>!r.enabled),"original hidden at "+phase);
                if(phase<.5f)
                {
                    LogBounds(folder,phase<.2f?"crack":"scatter",meshes,room.Find("Floor (2, 6)"),fragments);
                    Test(MeasureOcclusion(folder,phase<.2f?"02-crack":"03-scatter",meshes,player)>0,"fragments contribute camera pixels "+phase);
                    if(early==null){pieces=meshes.Select(r=>r.transform).ToArray(); early=pieces.Select(t=>t.localToWorldMatrix).ToArray();}
                    else
                    {
                        int moved=pieces.Where((t,i)=>t.localToWorldMatrix!=early[i]).Count(); Test(moved>1,"moving mesh transforms="+moved+"/"+pieces.Length);
                        host.panels.Pause(); var pose=pieces.Select(t=>t.localToWorldMatrix).ToArray(); float clock=host.Run.RecoveryRemaining;
                        for(int n=0;n<30;n++) yield return null;
                        Test(host.panels.Modal && clock==host.Run.RecoveryRemaining && pieces.Select((t,i)=>t.localToWorldMatrix==pose[i]).All(x=>x),"30-frame pause freezes clock and every mesh"); host.panels.Back();
                    }
                }
                else
                {
                    var leaves=tree.GetComponentsInChildren<Renderer>(true);
                    var bounds=leaves[0].bounds; foreach(var leaf in leaves) bounds.Encapsulate(leaf.bounds);
                    Test(!fragments.gameObject.activeInHierarchy && tree.position==home && bounds.size.y>height && MeasureOcclusion(folder,phase<.8f?"04-regrow":"05-grown",leaves,player)>0,"visible tree growing at home height="+bounds.size.y);height=bounds.size.y;
                }
            }
            while(host.Run.Recovering) yield return null; yield return null;
            Test(!tree && !fragments && pieces.All(t=>!t) && box.position==home && host.Run.Boxes[box.name]==host.Run.RecoveryCell(box.name) && cargo.Select((r,i)=>r.enabled==originals[i]).All(x=>x) && Pixels(folder,"06-restored",cargo)>0,"transient meshes disposed, visible log and logical cell restored "+home);
            Require(errors==0,"failed forest checks="+errors);
        }
    }
}


