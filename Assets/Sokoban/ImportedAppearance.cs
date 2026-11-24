/**
 * @file ImportedAppearance.cs
 * @brief Apply resolves room bindings and builds the visible hierarchy at its grid anchor. Load verifies cached GLB bytes and imports geometry or clips.
 * FitPose centers and sizes the visual footprint without changing the cell or the vertical motion. LateUpdate fits the evaluated animation pose.
 * SetGhost substitutes the preview material across every submesh. ReadImage decodes UI and surface images. OnDestroy releases instance-owned assets after pending imports finish.
 */
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using Newtonsoft.Json.Linq;
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
        Texture2D image;
        Material ghost;
        bool loading;
        public string Error { get; private set; }
        public Task Ready { get; private set; }

        public void Apply(JObject room, JToken visual)
        {
            Ready = Build(room["assets"], visual);
        }

        async Task<GltfImport> Load(JToken asset)
        {
            var importer = new GltfImport(deferAgent: new UninterruptedDeferAgent());
            imports.Add(importer);
            bool success = await importer.Load(RoomAssets.Bytes(asset), cancellationToken: lifetime.Token);
            lifetime.Token.ThrowIfCancellationRequested();
            RoomFile.Check(success, "无法解码 GLB：" + asset["name"]);
            return importer;
        }

        async Task Build(JToken assets, JToken visual)
        {
            loading = true;
            try
            {
                string modelId = (string)visual["model"];
                var clipRoot = gameObject;
                if (modelId != null)
                {
                    var model = await Load(assets[modelId]);
                    footprintRoot = new GameObject("单格外观").transform;
                    footprintRoot.SetParent(transform, false);
                    clipRoot = footprintRoot.gameObject;
                    var instantiator = new GameObjectInstantiator(model, footprintRoot, settings: new InstantiationSettings { SceneObjectCreation = SceneObjectCreation.Never, Mask = ComponentType.Mesh | ComponentType.Animation });
                    bool success = await model.InstantiateMainSceneAsync(instantiator, lifetime.Token);
                    lifetime.Token.ThrowIfCancellationRequested();
                    RoomFile.Check(success && GetComponentInChildren<Renderer>(true), "模型主场景缺少可见网格");
                }
                if (footprintRoot)
                {
                    footprint = new GridFootprint(footprintRoot);
                    var original = FitPose();
                    SizeReport = GridFootprint.Matches(original) ? "1 × 1 格" : "原始 " + GridFootprint.Describe(original) + " → 居中 1 × 1 格";
                }
                string motionId = (string)visual["animation"];
                if (motionId != null)
                {
                    var motion = await Load(assets[motionId]);
                    var clips = motion.GetAnimationClips();
                    RoomFile.Check(clips != null && clips.Length > 0, "动作未包含可播放的动画");
                    var player = clipRoot.GetComponent<Animation>() ?? clipRoot.AddComponent<Animation>();
                    foreach (var clip in clips) player.AddClip(clip, clip.name);
                    player.clip = clips[0];
                }
                var animation = playback = clipRoot.GetComponent<Animation>();
                if (animation && animation.clip && Application.isPlaying)
                {
                    animation.wrapMode = WrapMode.Loop;
                    animation.playAutomatically = true;
                    animation.Play(animation.clip.name);
                }
                string textureId = (string)visual["texture"];
                if (textureId != null)
                {
                    image = ReadImage(assets[textureId]);
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

        Bounds FitPose()
        {
            footprintRoot.localScale = Vector3.one;
            footprintRoot.localPosition = Vector3.zero;
            var bounds = footprint.Measure();
            var scale = new Vector3(1 / bounds.size.x, 1, 1 / bounds.size.z);
            footprintRoot.localScale = scale;
            footprintRoot.localPosition = new Vector3(-bounds.center.x * scale.x, 0, -bounds.center.z * scale.z);
            return bounds;
        }

        void LateUpdate()
        {
            if (footprint != null && playback && playback.isPlaying) FitPose();
        }

        public static Texture2D ReadImage(JToken asset)
        {
            var bytes = RoomAssets.Bytes(asset);
            var texture = new Texture2D(2, 2) { name = (string)asset["name"], hideFlags = HideFlags.DontSave };
            if (texture.LoadImage(bytes, true)) return texture;
            DisposeObject(texture);
            throw new FormatException("图像解码失败：" + asset["name"]);
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
