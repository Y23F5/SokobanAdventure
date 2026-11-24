/**
 * @file AssetThumbnail.cs
 * @brief Get caches a thumbnail per content ID or built-in Prefab for the palette lifetime. Render asynchronously instantiates real geometry and frames its renderer bounds using an orthographic camera. Dispose releases completed and pending previews.
 */
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sokoban
{
    public sealed class AssetThumbnail : IDisposable
    {
        readonly Dictionary<string,Task<Texture2D>> cache = new Dictionary<string,Task<Texture2D>>();
        readonly Dictionary<string,string> sizes = new Dictionary<string,string>();
        public string SizeReport(string key) => sizes.TryGetValue(key, out var report) ? report : "";
        bool disposed;
        public Task<Texture2D> Get(JObject room,string key,bool builtin=false)
        {
            if (!cache.TryGetValue(key,out var task)) cache[key] = task = Render(room,key,builtin);
            return task;
        }
        async Task<Texture2D> Render(JObject room,string key,bool builtin)
        {
            GameObject stage=null;
            RenderTexture target=null;
            Texture2D image=null;
            try
            {
                await Task.Yield();
                if (disposed) return null;
                if (!builtin && (string)room["assets"][key]["kind"] != "model")
                    return (string)room["assets"][key]["kind"] == "animation" ? null : ImportedAppearance.ReadImage(room["assets"][key]);
                stage=new GameObject("素材缩略图") { hideFlags=HideFlags.HideAndDontSave };
                stage.transform.position=new Vector3(0,-10000,0);
                if (builtin) Object.Instantiate(Resources.Load<GameObject>(key),stage.transform,false);
                else
                {
                    var appearance=stage.AddComponent<ImportedAppearance>();
                    appearance.Apply(room,new JObject { ["model"]=key });
                    await appearance.Ready;
                    if(disposed) return null;
                    sizes[key] = appearance.Error ?? appearance.SizeReport;
                    if(appearance.Error!=null) return null;
                }
                var renderers=stage.GetComponentsInChildren<Renderer>();
                if (renderers.Length==0) return null;
                var bounds=renderers[0].bounds;
                foreach(var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                foreach(var t in stage.GetComponentsInChildren<Transform>()) {t.gameObject.layer=31;t.gameObject.hideFlags=HideFlags.HideAndDontSave;}
                var cameraObject=new GameObject("素材相机");cameraObject.transform.SetParent(stage.transform,false);
                var cam=cameraObject.AddComponent<Camera>();cam.enabled=false;cam.orthographic=true;
                cam.orthographicSize=Mathf.Max(.05f,bounds.extents.magnitude)*1.08f;
                cam.transform.rotation=Quaternion.Euler(28,35,0);
                cam.transform.position=bounds.center-cam.transform.forward*(cam.orthographicSize*3+1);
                cam.farClipPlane=cam.orthographicSize*6+2;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color32(237,216,177,255);cam.cullingMask=1<<31;
                target=RenderTexture.GetTemporary(96,96,24);cam.targetTexture=target;
                var previous=RenderTexture.active;
                try {cam.Render();RenderTexture.active=target;image=new Texture2D(96,96,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,96,96),0,0);image.Apply(false,true);}
                finally {RenderTexture.active=previous;cam.targetTexture=null;}
                return image;
            }
            catch(Exception ex){DestroyTemporary(image);Debug.LogWarning("素材缩略图："+ex.Message);return null;}
            finally {if(stage)stage.SetActive(false);DestroyTemporary(stage);if(target)RenderTexture.ReleaseTemporary(target);}
        }
        public void Dispose()
        {
            disposed=true;
            foreach(var task in cache.Values) if(task.Status==TaskStatus.RanToCompletion) DestroyTemporary(task.Result);
            cache.Clear();
        }
        static void DestroyTemporary(Object obj) { if (!obj) return; if(Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); }
    }
}
