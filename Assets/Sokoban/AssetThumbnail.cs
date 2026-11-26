/**
 * @file AssetThumbnail.cs
 * @brief 按内容 ID 或内置 Prefab 缓存缩略图，生命周期与素材栏一致；资源取自 Core.AssetDescriptor 列表。
 * Get(assets, key, builtin)：缓存并返回缩略图任务，内置外观 key 是 Resources 路径，导入模型 key 是资源 ID，贴图与界面图直接解码，动作没有缩略图。
 * Render：在离屏舞台实例化真实几何并按包围盒取景，导入模型的尺寸问题记入 modelIssues，与位图是否生成无关。
 * ModelIssue(assets, key)、SizeReport(key)：对外公开单格模型校验结果与实测占地，预览未完成时返回提示。
 * Dispose：释放全部预览位图。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sokoban.Core;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Sokoban
{
    public sealed class AssetThumbnail : IDisposable
    {
        readonly Dictionary<string, Task<Texture2D>> cache = new Dictionary<string, Task<Texture2D>>();
        readonly Dictionary<string, string> sizes = new Dictionary<string, string>();
        readonly Dictionary<string, string> modelIssues = new Dictionary<string, string>();
        bool disposed;

        static AssetDescriptor Find(IEnumerable<AssetDescriptor> assets, string key) => assets?.FirstOrDefault(a => a.Id == key);

        public string SizeReport(string key) => sizes.TryGetValue(key, out var report) ? report : "";

        public string ModelIssue(IReadOnlyList<AssetDescriptor> assets, string key)
        {
            if (Find(assets, key)?.Kind != AssetKinds.Model) return null;
            var preview = Get(assets, key);
            if (!preview.IsCompleted) return "正在检查模型尺寸";
            return modelIssues.TryGetValue(key, out string issue) ? issue : null;
        }

        public Task<Texture2D> Get(IReadOnlyList<AssetDescriptor> assets, string key, bool builtin = false)
        {
            if (!cache.TryGetValue(key, out var task)) cache[key] = task = Render(assets, key, builtin);
            return task;
        }

        async Task<Texture2D> Render(IReadOnlyList<AssetDescriptor> assets, string key, bool builtin)
        {
            GameObject stage = null;
            RenderTexture target = null;
            Texture2D image = null;
            try
            {
                await Task.Yield();
                if (disposed) return null;
                var asset = builtin ? null : Find(assets, key);
                if (!builtin && asset == null) return null;
                if (!builtin && asset.Kind != AssetKinds.Model)
                    return asset.Kind == AssetKinds.Animation ? null : ImportedAppearance.ReadImage(asset);
                stage = new GameObject("素材缩略图") { hideFlags = HideFlags.HideAndDontSave };
                stage.transform.position = new Vector3(0, -10000, 0);
                if (builtin) Object.Instantiate(Resources.Load<GameObject>(key), stage.transform, false);
                else
                {
                    var appearance = stage.AddComponent<ImportedAppearance>();
                    appearance.Apply(assets, key);
                    await appearance.Ready;
                    if (disposed) return null;
                    string issue = appearance.Error ?? appearance.FootprintIssue;
                    if (issue != null) modelIssues[key] = issue;
                    sizes[key] = issue ?? appearance.SizeReport;
                    if (appearance.Error != null) return null;
                }
                var renderers = stage.GetComponentsInChildren<Renderer>();
                if (renderers.Length == 0) return null;
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                foreach (var t in stage.GetComponentsInChildren<Transform>()) { t.gameObject.layer = 31; t.gameObject.hideFlags = HideFlags.HideAndDontSave; }
                var cameraObject = new GameObject("素材相机"); cameraObject.transform.SetParent(stage.transform, false);
                var cam = cameraObject.AddComponent<Camera>(); cam.enabled = false; cam.orthographic = true;
                cam.orthographicSize = Mathf.Max(.05f, bounds.extents.magnitude) * 1.08f;
                cam.transform.rotation = Quaternion.Euler(28, 35, 0);
                cam.transform.position = bounds.center - cam.transform.forward * (cam.orthographicSize * 3 + 1);
                cam.farClipPlane = cam.orthographicSize * 6 + 2; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color32(237, 216, 177, 255); cam.cullingMask = 1 << 31;
                target = RenderTexture.GetTemporary(96, 96, 24); cam.targetTexture = target;
                var previous = RenderTexture.active;
                try { cam.Render(); RenderTexture.active = target; image = new Texture2D(96, 96, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, 96, 96), 0, 0); image.Apply(false, true); }
                finally { RenderTexture.active = previous; cam.targetTexture = null; }
                return image;
            }
            catch (Exception ex) { DestroyTemporary(image); Debug.LogWarning("素材缩略图：" + ex.Message); return null; }
            finally { if (stage) stage.SetActive(false); DestroyTemporary(stage); if (target) RenderTexture.ReleaseTemporary(target); }
        }

        public void Dispose()
        {
            disposed = true;
            foreach (var task in cache.Values) if (task.Status == TaskStatus.RanToCompletion) DestroyTemporary(task.Result);
            cache.Clear();
        }

        static void DestroyTemporary(Object obj) { if (!obj) return; if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); }
    }
}
