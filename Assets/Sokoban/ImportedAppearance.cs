/**
 * @file ImportedAppearance.cs
 * @brief 按 Core.AssetDescriptor 解析资源绑定并在格锚点下构建可见层级，GLB 字节取自 RoomAssets 的内容缓存，Core 不持有字节。
 * Apply(assets, modelId, animationId, textureId, sampleExternally)：把资源表建成字典后异步构建，模型、动作、贴图各取一个资源 ID，动作可由外部时钟采样。
 * SampleMotion(fraction)：按恢复时钟采样已配置的动作片段。
 * InspectModel(asset)：离屏加载模型并实测水平包围盒，不是 1×1 格（容差 0.001）时抛 InvalidOperationException。
 * Load(asset)：校验缓存字节并导入几何或动作片段。Build 依次建模型、量单格占地、挂动作、贴图并复制材质。
 * LateUpdate：动画播放中首次出现超出单格的姿态时记录 AnimationIssue 并保持原姿态。
 * ReadImage(asset)：解码界面图与表面贴图。SetGhost(material)：把预览材质替换到所有子网格。OnDestroy：等待导入结束后释放实例持有的资源。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban
{
    [ExecuteAlways]
    public sealed class ImportedAppearance : MonoBehaviour
    {
        readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        readonly List<GltfImport> imports = new List<GltfImport>();
        readonly List<Material> materials = new List<Material>();
        Transform footprintRoot;
        GridFootprint footprint;
        Animation playback;
        public string SizeReport { get; private set; }
        public string FootprintIssue { get; private set; }
        public string AnimationIssue { get; private set; }
        string modelName;
        Texture2D image;
        Material ghost;
        bool loading;
        bool sampledMotion;
        public string Error { get; private set; }
        public Task Ready { get; private set; }

        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        public void Apply(IEnumerable<AssetDescriptor> assets, string modelId, string animationId = null, string textureId = null, bool sampleExternally = false)
        {
            sampledMotion = sampleExternally;
            var library = new Dictionary<string, AssetDescriptor>(StringComparer.Ordinal);
            foreach (var asset in assets ?? Enumerable.Empty<AssetDescriptor>()) library[asset.Id] = asset;
            Ready = Build(library, modelId, animationId, textureId);
        }

        internal void SampleMotion(float fraction)
        {
            if (playback && playback.clip) playback.clip.SampleAnimation(playback.gameObject, Mathf.Clamp01(fraction) * playback.clip.length);
        }

        public static async Task InspectModel(AssetDescriptor asset)
        {
            var stage = new GameObject("模型尺寸检查") { hideFlags = HideFlags.HideAndDontSave };
            stage.transform.position = Vector3.down * 10000;
            try
            {
                var appearance = stage.AddComponent<ImportedAppearance>();
                appearance.Apply(new[] { asset }, asset.Id);
                await appearance.Ready;
                string issue = appearance.Error ?? appearance.FootprintIssue;
                Check(issue == null, issue);
            }
            finally { DisposeObject(stage); }
        }

        async Task<GltfImport> Load(AssetDescriptor asset)
        {
            var importer = new GltfImport(deferAgent: new UninterruptedDeferAgent());
            imports.Add(importer);
            bool success = await importer.Load(RoomAssets.Bytes(asset), cancellationToken: lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            Check(success, "无法解码 GLB：" + asset.Name);
            return importer;
        }

        static AssetDescriptor Need(Dictionary<string, AssetDescriptor> library, string id)
        {
            Check(library.TryGetValue(id, out var asset), "缺少绑定素材 " + id);
            return asset;
        }

        async Task Build(Dictionary<string, AssetDescriptor> library, string modelId, string animationId, string textureId)
        {
            loading = true;
            try
            {
                var clipRoot = gameObject;
                if (modelId != null)
                {
                    var asset = Need(library, modelId);
                    modelName = asset.Name;
                    var model = await Load(asset);
                    footprintRoot = new GameObject("单格外观").transform;
                    footprintRoot.SetParent(transform, false);
                    clipRoot = footprintRoot.gameObject;
                    var instantiator = new GameObjectInstantiator(model, footprintRoot, settings: new InstantiationSettings { SceneObjectCreation = SceneObjectCreation.Never, Mask = ComponentType.Mesh | ComponentType.Animation });
                    bool success = await model.InstantiateMainSceneAsync(instantiator, lifetime.Token);
                    lifetime.Token.ThrowIfCancellationRequested();
                    Check(success && GetComponentInChildren<Renderer>(true), "模型主场景缺少可见网格");
                }
                if (footprintRoot)
                {
                    footprint = new GridFootprint(footprintRoot);
                    var original = footprint.Measure();
                    footprintRoot.localPosition = new Vector3(-original.center.x, 0, -original.center.z);
                    SizeReport = GridFootprint.Describe(original);
                    if (!GridFootprint.HasUnitSize(original))
                        FootprintIssue = $"模型「{modelName}」占地 {SizeReport}，需要 1 × 1 格（宽和深各允许 ±0.001 格）";
                }
                if (animationId != null)
                {
                    var motion = await Load(Need(library, animationId));
                    var clips = motion.GetAnimationClips();
                    Check(clips != null && clips.Length > 0, "动作未包含可播放的动画");
                    var player = clipRoot.GetComponent<Animation>() ?? clipRoot.AddComponent<Animation>();
                    foreach (var clip in clips) player.AddClip(clip, clip.name);
                    player.clip = clips[0];
                }
                var animation = playback = clipRoot.GetComponent<Animation>();
                if (animation && animation.clip && sampledMotion) { animation.playAutomatically = false; animation.Stop(); }
                else if (animation && animation.clip && Application.isPlaying)
                {
                    animation.wrapMode = WrapMode.Loop;
                    animation.playAutomatically = true;
                    animation.Play(animation.clip.name);
                }
                if (textureId != null)
                {
                    image = ReadImage(Need(library, textureId));
                    foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                    {
                        var slots = renderer.sharedMaterials;
                        for (int i = 0; i < slots.Length; i++)
                        {
                            if (!slots[i]) continue;
                            var copy = new Material(slots[i]) { mainTexture = image, hideFlags = HideFlags.DontSave };
                            materials.Add(copy);
                            slots[i] = copy;
                        }
                        renderer.sharedMaterials = slots;
                    }
                }
                foreach (var child in GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.DontSave;
                if (ghost) SetGhost(ghost);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (this) { Error = ex.Message; Debug.LogError("素材 " + name + "：" + Error, this); } }
            finally { loading = false; if (lifetime.IsCancellationRequested) Release(); }
        }

        void LateUpdate()
        {
            if (AnimationIssue != null || footprint == null || !playback || !playback.isPlaying || !footprint.TryMeasure(out var bounds)) return;
            bounds.center += footprintRoot.localPosition;
            if (GridFootprint.Matches(bounds)) return;
            var state = playback[playback.clip.name];
            string time = state == null ? "" : $" {state.time:0.000} 秒";
            AnimationIssue = $"模型「{modelName}」的动作「{playback.clip.name}」{time}占地 {GridFootprint.Describe(bounds)}，中心偏移 ({bounds.center.x:0.####}, {bounds.center.z:0.####}) 格，超出单格要求";
            Debug.LogWarning(AnimationIssue, this);
        }

        public static Texture2D ReadImage(AssetDescriptor asset)
        {
            var bytes = RoomAssets.Bytes(asset);
            var texture = new Texture2D(2, 2) { name = asset.Name, hideFlags = HideFlags.DontSave };
            if (texture.LoadImage(bytes, true)) return texture;
            DisposeObject(texture);
            throw new FormatException("图像解码失败：" + asset.Name);
        }

        public void SetGhost(Material material)
        {
            ghost = material;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++) slots[i] = material;
                renderer.sharedMaterials = slots;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        void OnDestroy() { lifetime.Cancel(); if (!loading) Release(); }

        void Release()
        {
            foreach (var importer in imports) importer.Dispose();
            foreach (var material in materials) DisposeObject(material);
            DisposeObject(image);
            footprint?.Dispose();
            lifetime.Dispose();
        }

        static void DisposeObject(UnityEngine.Object obj)
        {
            if (!obj) return;
            if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
        }
    }
}
