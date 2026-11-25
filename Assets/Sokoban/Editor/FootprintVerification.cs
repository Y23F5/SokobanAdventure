/**
 * @file FootprintVerification.cs
 * @brief Run enters the actual runtime editor in Play Mode. Verify uses the shared file dialog and inspects the animated imported hierarchy.
 * Tick advances one frame at a time and returns the result to Unity batch mode.
 */
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class FootprintVerification
    {
        const string Key = "Sokoban.FootprintVerification";
        static IEnumerator checks;
        static int frame = -1;

        public static void Run()
        {
            var folder = Environment.GetEnvironmentVariable("SOKOBAN_FOOTPRINT_FIXTURES");
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
            EditorSceneManager.OpenScene("Assets/Scenes/RuntimeEditor.unity");
            SessionState.SetString(Key, folder);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Hook() => EditorApplication.update += Tick;

        static void Tick()
        {
            var folder = SessionState.GetString(Key, "");
            if (!EditorApplication.isPlaying || folder.Length == 0 || Time.frameCount == frame) return;
            frame = Time.frameCount;
            int status = 0;
            try { if ((checks ??= Verify(folder)).MoveNext()) return; }
            catch (Exception failure) { Debug.LogException(failure); status = 1; }
            SessionState.EraseString(Key);
            EditorApplication.Exit(status);
        }

        static void Submit(LayoutPanel panel, string path)
        {
            panel.ChooseFile("导入模型");
            panel.Filename = path;
            panel.SubmitFile();
        }

        static IEnumerator Verify(string folder)
        {
            yield return null;
            yield return null;
            var panel = UnityEngine.Object.FindObjectOfType<LayoutHost>().Panel;
            var draft = panel.Draft;
            var before = draft.Snapshot();
            Submit(panel, Path.Combine(folder, "TooWide.glb"));
            float deadline = Time.realtimeSinceStartup + 10;
            while (panel.Status == "正在检查模型尺寸" && Time.realtimeSinceStartup < deadline) yield return null;
            bool unchanged = JToken.DeepEquals(before, draft.Snapshot());
            Debug.Log($"FOOTPRINT IMPORT 操作={panel.Operation}，提示={panel.Status}，草稿未改={unchanged}");
            Require(panel.Operation == "导入模型" && panel.Status.Contains("TooWide") && panel.Status.Contains("1.4") &&
                panel.Status.Contains("需要 1 × 1 格") && unchanged, "宽 1.4 格提示实测，草稿及撤销历史不变");

            panel.CancelDialog();
            Submit(panel, Path.Combine(folder, "Carrier.glb"));
            deadline = Time.realtimeSinceStartup + 10;
            while (panel.Operation != null && Time.realtimeSinceStartup < deadline) yield return null;
            Require(panel.Operation == null && panel.Status.Contains("已导入素材"), "原生单格 Carrier 通过真实导入入口");

            foreach (string model in new[] { "Carrier", "Overstep" })
            {
                string id = draft.ImportAsset(Path.Combine(folder, model + ".glb"), "model");
                var anchor = new GameObject(model);
                var appearance = anchor.AddComponent<ImportedAppearance>();
                appearance.Apply(draft.Room, new JObject { ["model"] = id });
                deadline = Time.realtimeSinceStartup + 10;
                while (!appearance.Ready.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
                Require(appearance.Ready.IsCompleted && appearance.Error == null && appearance.FootprintIssue == null, model + " 初始原始宽深符合要求");
                var visual = anchor.transform.Find("单格外观");
                var position = visual.localPosition;
                deadline = Time.realtimeSinceStartup + 1.2f;
                float maxDepth = 0, maxWidth = 0;
                using (var measurement = new GridFootprint(visual))
                    while (Time.realtimeSinceStartup < deadline)
                    {
                        if (visual.localScale != Vector3.one || visual.localPosition != position)
                            throw new InvalidOperationException(model + " 的外层变换被动画诊断修改");
                        if (measurement.TryMeasure(out var bounds))
                        {
                            maxWidth = Mathf.Max(maxWidth, bounds.size.x);
                            maxDepth = Mathf.Max(maxDepth, bounds.size.z);
                        }
                        yield return null;
                    }
                if (model == "Carrier")
                {
                    Require(appearance.AnimationIssue == null && Mathf.Abs(maxWidth - 1) <= .001f && Mathf.Abs(maxDepth - 1) <= .001f,
                        "Carrier 保留原比例，其升降过程无超格警告");
                    var skin = visual.GetComponentInChildren<SkinnedMeshRenderer>();
                    skin.enabled = false;
                    yield return null;
                    yield return null;
                    using (var hidden = new GridFootprint(visual))
                        Require(!hidden.TryMeasure(out _) && appearance.AnimationIssue == null && visual.localScale == Vector3.one, "完全隐藏帧无误报或姿态改变");
                    skin.enabled = true;
                }
                else
                    Require(maxWidth > 1.1f && appearance.AnimationIssue != null && appearance.AnimationIssue.Contains("Overstep"),
                        "越界动画保留实际宽度并报告，而外层变换始终不变");
                UnityEngine.Object.Destroy(anchor);
            }

            var legacy = new RoomDraft(Resources.Load<TextAsset>("DefaultRoom").text);
            string wide = legacy.ImportAsset(Path.Combine(folder, "TooWide.glb"), "model");
            legacy.SelectAsset(wide);
            legacy.Click(new Vector2Int(2, 4));
            string legacyPath = Path.Combine(folder, "legacy.room.json");
            legacy.ExportRoom(legacyPath);
            legacy.Open(legacyPath, false);
            var root = new GameObject("旧房回读");
            var room = new RoomPreview().BuildRoom(legacy.Room, Vector2Int.zero, null, root.transform);
            var existing = room.GetComponentInChildren<ImportedAppearance>();
            deadline = Time.realtimeSinceStartup + 10;
            while (!existing.Ready.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
            var oldVisual = existing.transform.Find("单格外观");
            using (var measured = new GridFootprint(oldVisual))
                Require(existing.Error == null && existing.FootprintIssue.Contains("1.4") && oldVisual.localScale == Vector3.one &&
                    Mathf.Abs(measured.Measure().size.x - 1.4f) <= .001f, "旧房仍可重开，保留原生 1.4 格宽度并提供资源诊断");
            UnityEngine.Object.Destroy(root);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Debug.Log("FOOTPRINT PASS " + message);
        }
    }
}
