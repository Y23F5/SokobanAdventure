/**
 * @file RecoveryPresenter.cs
 * @brief 恢复演出的表现层：只调用恢复方法取每帧零件姿态并应用到零件池，不按主题名分支，不读写规则状态。
 * Sample(run, group, boxOf, t)：对每个需要归位的箱子，用恢复道具的脚本在进度 t 取姿态；id 等于箱子 ID 的零件驱动真箱子，其余零件按 resource 加 child 从零件池取实例；默认外观的可开合地形格用默认外观脚本取姿态，格位与朝向作为 params（gateX、gateZ、yaw）传入。
 * Clear(boxOf)：销毁零件池并恢复被隐藏的箱子渲染器，恢复完成或读档重建时调用。
 * Part(task, part, group)：按 resource 实例化预制件，child 非空时取该子节点作为零件；预制件或子节点找不到时 LogWarning 并跳过该零件，恢复照常提交。
 * Apply、ApplyBox：把位置、欧拉角、缩放与可见度写到变换上，动画剪辑按脚本给出的时刻（秒，不超过剪辑长度）采样。
 * 脚本出错只警告一次，不显示演出，规则层照常提交恢复。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Sokoban.Core;
using Object = UnityEngine.Object;

namespace Sokoban
{
    internal sealed class RecoveryPresenter
    {
        readonly LuaHost host = new LuaHost();
        readonly Dictionary<string, IRecoveryMethod> methods = new Dictionary<string, IRecoveryMethod>();
        readonly Dictionary<string, GameObject> pool = new Dictionary<string, GameObject>();
        readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
        readonly HashSet<string> skipped = new HashSet<string>();
        readonly HashSet<string> warned = new HashSet<string>();
        readonly Dictionary<Transform, Renderer[]> hidden = new Dictionary<Transform, Renderer[]>();

        internal RecoveryPresenter() { host.Register(LuaRecoveryMethod.CreateEntry()); }

        IRecoveryMethod Method(string scriptRef)
        {
            if (methods.TryGetValue(scriptRef, out var cached)) return cached;
            var asset = Resources.Load<TextAsset>("RecoveryScripts/" + scriptRef + ".lua");
            if (!asset) Warn("script/" + scriptRef, "缺少恢复脚本 " + scriptRef + "，改用默认滑行");
            IRecoveryMethod method = asset ? new LuaRecoveryMethod(host, asset.text) : new SlideMethod();
            methods[scriptRef] = method;
            return method;
        }

        void Warn(string key, string message) { if (warned.Add(key)) Debug.LogWarning(message); }

        internal void Sample(PuzzleRun run, Transform group, Func<string, Transform> boxOf, float t)
        {
            var item = run.RunningRecoveryItem;
            if (item == null) return;
            var method = Method(item.ScriptRef);
            foreach (var move in run.RecoveryMoves())
            {
                var ctx = new RecoveryContext(run.ActiveId, move.BoxId, RecoveryRules.Route(move.From, move.Home), run.RecoveryDuration, item.Params);
                Play(method, ctx, move.BoxId, boxOf(move.BoxId), group, t);
            }
            var gates = run.RecoveryGates();
            if (gates.Count == 0) return;
            var gateMethod = Method(PuzzleRun.DefaultGateScript);
            foreach (var gate in gates)
            {
                var parameters = new Dictionary<string, float> { ["gateX"] = gate.Cell.X, ["gateZ"] = gate.Cell.Y, ["yaw"] = gate.Yaw };
                string task = "gate-" + gate.Cell.X + "-" + gate.Cell.Y;
                var ctx = new RecoveryContext(run.ActiveId, task, new List<Cell> { gate.Cell }, run.RecoveryDuration, parameters);
                Play(gateMethod, ctx, task, null, group, t);
            }
        }

        void Play(IRecoveryMethod method, RecoveryContext ctx, string task, Transform box, Transform group, float t)
        {
            IReadOnlyList<RecoveryPart> parts;
            try { parts = method.Pose(t, ctx); }
            catch (InvalidOperationException error) { Warn("pose/" + task, "恢复演出脚本出错，不显示演出：" + error.Message); return; }
            foreach (var part in parts)
            {
                if (box && part.PartId == ctx.BoxId) { ApplyBox(box, part); continue; }
                var go = Part(task, part, group);
                if (!go) continue;
                Apply(go, part);
            }
        }

        GameObject Part(string task, RecoveryPart part, Transform group)
        {
            string key = task + "/" + part.PartId;
            if (pool.TryGetValue(key, out var existing)) return existing;
            if (skipped.Contains(key)) return null;
            if (string.IsNullOrEmpty(part.Resource) || !(Prefab(part.Resource) is GameObject prefab))
            {
                skipped.Add(key);
                Warn(key, "恢复零件 " + part.PartId + " 缺少资源 " + part.Resource + "，已跳过");
                return null;
            }
            var instance = Object.Instantiate(prefab, group, false);
            instance.name = part.PartId;
            instance.hideFlags = HideFlags.DontSave;
            if (!string.IsNullOrEmpty(part.Child))
            {
                var child = Find(instance.transform, part.Child);
                if (!child)
                {
                    skipped.Add(key);
                    Warn(key, "恢复零件 " + part.PartId + " 在 " + part.Resource + " 中找不到子节点 " + part.Child + "，已跳过");
                    Object.Destroy(instance);
                    return null;
                }
                child.SetParent(group, false);
                Object.Destroy(instance);
                instance = child.gameObject;
            }
            var animator = instance.GetComponent<Animator>();
            if (animator) animator.enabled = false;
            pool[key] = instance;
            return instance;
        }

        GameObject Prefab(string resource)
        {
            if (!prefabs.TryGetValue(resource, out var prefab)) prefabs[resource] = prefab = Resources.Load<GameObject>(resource);
            return prefab;
        }

        static Transform Find(Transform root, string name)
        {
            var queue = new Queue<Transform>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var at = queue.Dequeue();
                if (at != root && at.name == name) return at;
                foreach (Transform child in at) queue.Enqueue(child);
            }
            return null;
        }

        AnimationClip Clip(string resource, string name)
        {
            string key = resource + "#" + name;
            if (clips.TryGetValue(key, out var clip)) return clip;
            var all = Resources.LoadAll<AnimationClip>(resource);
            clip = all.FirstOrDefault(c => c.name == name) ?? all.FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            clips[key] = clip;
            return clip;
        }

        void Apply(GameObject go, RecoveryPart part)
        {
            var target = go.transform;
            target.localPosition = new Vector3(part.X, part.Y, part.Z);
            if (part.Yaw != 0 || part.Pitch != 0 || part.Roll != 0) target.localRotation = Quaternion.Euler(part.Pitch, part.Yaw, part.Roll);
            target.localScale = new Vector3(part.ScaleX, part.ScaleY, part.ScaleZ) * part.Scale;
            go.SetActive(part.Visible);
            if (!part.Visible || part.Clip == null || !part.ClipTime.HasValue) return;
            var clip = Clip(part.Resource, part.Clip);
            if (clip) clip.SampleAnimation(go, Mathf.Clamp(part.ClipTime.Value, 0, clip.length));
        }

        void ApplyBox(Transform box, RecoveryPart part)
        {
            box.localPosition = new Vector3(part.X, part.Y, part.Z);
            if (part.Yaw != 0 || part.Pitch != 0 || part.Roll != 0) box.localRotation = Quaternion.Euler(part.Pitch, part.Yaw, part.Roll);
            box.localScale = new Vector3(part.ScaleX, part.ScaleY, part.ScaleZ) * part.Scale;
            if (!hidden.TryGetValue(box, out var renderers)) hidden[box] = renderers = box.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers) if (renderer) renderer.enabled = part.Visible;
        }

        internal void Clear()
        {
            foreach (var go in pool.Values) if (go) { go.SetActive(false); Object.Destroy(go); }
            pool.Clear();
            skipped.Clear();
            foreach (var pair in hidden)
            {
                if (!pair.Key) continue;
                pair.Key.localScale = Vector3.one;
                foreach (var renderer in pair.Value) if (renderer) renderer.enabled = true;
            }
            hidden.Clear();
        }
    }
}
