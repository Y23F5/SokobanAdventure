/**
 * @file SceneReloadVerification.cs
 * @brief Run 在隔离草稿槽创建整图现场；Prepare 保存对照并请求真实脚本域重载。
 * ResumeAfterReload 从编辑器会话标记恢复测试；Verify 比较草稿快照字符串、箱子位置、模型、相机与文件路径，再测试独立房间。
 * ModelState 从活动场景读取房间名、对象、格子和模型位置，拒绝重复根与残留虚影。
 * 草稿快照不含撤销历史、选择与工具，这些不在重载断言范围内；相机由槽文件旁的视角文件保存。
 */
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Sokoban.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sokoban.Editor
{
    public static class SceneReloadVerification
    {
        const string PhaseKey = "Sokoban.QA.ReloadPhase";
        static string Folder => Environment.GetEnvironmentVariable("SOKOBAN_EDITOR_TEST_SLOT");
        static string SlotFile => Path.Combine(Folder, "unity-editor-ws02.json");

        public static void Run()
        {
            if (!Application.isBatchMode || string.IsNullOrEmpty(Folder) || Directory.Exists(Folder))
                throw new InvalidOperationException("请为隔离批处理指定新的草稿目录");
            Directory.CreateDirectory(Folder);
            EditorSceneManager.OpenScene(WorldSceneSession.GamePath);
            WorldSceneSession.Tick();
            WorldSceneSession.Panel.Draft.Save(Path.Combine(Folder, "map.world.json"), false);
            Prepare("world");
        }

        static JArray ModelState()
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            var root = roots.Single(item => item.name.StartsWith("Room • ", StringComparison.Ordinal));
            if (roots.Any(item => item.name == "房间放置预览")) throw new InvalidDataException("残留虚影");
            var result = new JArray();
            foreach (Transform room in root.transform) foreach (Transform entity in room)
            {
                var position = entity.position;
                result.Add(new JArray(room.name, entity.name, position.x, position.y, position.z,
                    entity.GetComponentsInChildren<Renderer>().Length));
            }
            return result;
        }

        static void Prepare(string phase)
        {
            var panel = WorldSceneSession.Panel;
            var draft = panel.Draft;
            if (draft.World != null) draft.UseRoom("factory");
            var box = draft.Room.ObjectsOf(ObjectCategories.Box).First();
            draft.Select(box.Cell);
            string movedId = draft.Selected;
            if (movedId != box.Id) throw new InvalidDataException("选择箱子失败");
            Cell? target = null;
            for (int y = 0; y < draft.Room.Height && !target.HasValue; y++)
                for (int x = 0; x < draft.Room.Width && !target.HasValue; x++)
                    if (draft.PlacementError(true, new Cell(x, y), movedId) == null && new Cell(x, y) != box.Cell) target = new Cell(x, y);
            if (!target.HasValue) throw new InvalidDataException("没有可移动箱子的合法格");
            draft.Move(target.Value);
            draft.Tool = RoomTool.Move;
            var view = EditorWindow.GetWindow<SceneView>(); view.Focus();
            view.LookAtDirect(new Vector3(12, 0, 9), Quaternion.Euler(52, 28, 0), 8); view.orthographic = true;
            WorldSceneSession.Tick(); panel.TrackView(view);
            if (!panel.Flush()) throw new IOException(panel.Status);
            var expected = new JObject
            {
                ["slot"] = File.ReadAllText(SlotFile),
                ["view"] = File.ReadAllText(SlotFile + ".view"),
                ["box"] = movedId,
                ["cell"] = new JArray(target.Value.X, target.Value.Y),
                ["models"] = ModelState()
            };
            File.WriteAllText(Path.Combine(Folder, phase + "-before.json"), expected.ToString());
            SessionState.SetString(PhaseKey, phase);
            EditorUtility.RequestScriptReload();
        }

        [InitializeOnLoadMethod]
        static void ResumeAfterReload()
        {
            if (Application.isBatchMode && !string.IsNullOrEmpty(Folder) && SessionState.GetString(PhaseKey, "") != "")
                EditorApplication.delayCall += Verify;
        }

        static void Verify()
        {
            string phase = SessionState.GetString(PhaseKey, ""); SessionState.EraseString(PhaseKey);
            try
            {
                WorldSceneSession.Tick(); var panel = WorldSceneSession.Panel;
                var expected = JObject.Parse(File.ReadAllText(Path.Combine(Folder, phase + "-before.json")));
                string snapshot = panel.Draft.Snapshot();
                if (snapshot != (string)expected["slot"]) throw new InvalidDataException("重载后草稿快照与重载前不同");
                var models = ModelState(); if (!JToken.DeepEquals(expected["models"], models)) throw new InvalidDataException("重载后模型不同");
                var f = ((string)expected["view"]).Split(',').Select(p => float.Parse(p, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                var view = EditorWindow.GetWindow<SceneView>();
                if (f.Length != 9 || Vector3.Distance(view.pivot, new Vector3(f[0], f[1], f[2])) > .001f || Quaternion.Angle(view.rotation, new Quaternion(f[3], f[4], f[5], f[6])) > .05f || Mathf.Abs(view.size - f[7]) > .001f || view.orthographic != (f[8] > 0))
                    throw new InvalidDataException("视角未完整还原");
                string id = (string)expected["box"];
                var moved = panel.Draft.Find(id);
                var cell = new Cell((int)expected["cell"][0], (int)expected["cell"][1]);
                if (moved == null || moved.Cell != cell) throw new InvalidDataException("重载后箱子不在移动后的格子");
                if (!panel.Draft.Dirty) throw new InvalidDataException("重载后未保存修改的脏标记丢失");
                File.AppendAllText(Path.Combine(Folder, "reload.txt"), "PASS " + phase + " 脚本域重载还原草稿快照、工作路径、脏标记、箱子位置、视角和 " + models.Count + " 个地形/对象/选中标记；单一预览根。\n");
                if (phase == "world") { panel.Draft.Open(Path.Combine(Application.streamingAssetsPath, "World", "forest.room.json"), true); panel.Draft.Save(Path.Combine(Folder, "solo.room.json"), false); Prepare("room"); }
                else EditorApplication.Exit(0);
            }
            catch (Exception error) { File.AppendAllText(Path.Combine(Folder, "reload.txt"), "FAIL " + phase + " " + error + "\n"); Debug.LogException(error); EditorApplication.Exit(1); }
        }
    }
}
