/**
 * @file SceneReloadVerification.cs
 * @brief Run 在隔离草稿槽创建整图现场；Prepare 保存对照并请求真实脚本域重载。
 * ResumeAfterReload 从编辑器会话标记恢复测试；Verify 比较数据、历史、模型、相机与文件路径，再测试独立房间。
 * ModelState 从活动场景读取房间名、对象、格子和模型位置，拒绝重复根与残留虚影。
 */
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
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

        public static void Run()
        {
            if (!Application.isBatchMode || string.IsNullOrEmpty(Folder) || Directory.Exists(Folder))
                throw new InvalidOperationException("请为隔离批处理指定新的草稿目录");
            Directory.CreateDirectory(Folder);
            EditorSceneManager.OpenScene(WorldSceneSession.GamePath);
            WorldSceneSession.Tick();
            WorldSceneSession.Panel.Draft.Save(Path.Combine(Folder,"map.world.json"),false);
            Prepare("world");
        }

        static JArray ModelState()
        {
            var roots = SceneManager.GetActiveScene().GetRootGameObjects();
            var root = roots.Single(item=>item.name.StartsWith("Room • ",StringComparison.Ordinal));
            if (roots.Any(item=>item.name=="房间放置预览")) throw new InvalidDataException("残留虚影");
            var result = new JArray();
            foreach (Transform room in root.transform) foreach (Transform entity in room)
            {
                var position=entity.position;
                result.Add(new JArray(room.name,entity.name,position.x,position.y,position.z,
                    entity.GetComponentsInChildren<Renderer>().Length));
            }
            return result;
        }

        static void Prepare(string phase)
        {
            var panel=WorldSceneSession.Panel;
            var draft=panel.Draft;
            if (draft.World!=null) draft.UseRoom("factory");
            var box=draft.Room["boxes"].First();
            draft.Select(RoomFile.At(box));
            var size=RoomFile.Size(draft.Room);
            var cell=Enumerable.Range(0,size.x*size.y).Select(i=>new Vector2Int(i%size.x,i/size.x)).First(point=>draft.PlacementError(true,point)==null);
            draft.Move(cell);draft.Undo();draft.Tool=RoomTool.Move;
            var view=EditorWindow.GetWindow<SceneView>();view.Focus();
            view.LookAtDirect(new Vector3(12,0,9),Quaternion.Euler(52,28,0),8);view.orthographic=true;
            WorldSceneSession.Tick();panel.TrackView(view);
            if (!panel.Flush()) throw new IOException(panel.Status);
            var expected = new JObject { ["slot"]=JObject.Parse(File.ReadAllText(Path.Combine(Folder,"unity-editor-ws02.json"))),["models"]=ModelState() };
            File.WriteAllText(Path.Combine(Folder,phase+"-before.json"),expected.ToString());
            SessionState.SetString(PhaseKey,phase);
            EditorUtility.RequestScriptReload();
        }

        [InitializeOnLoadMethod]
        static void ResumeAfterReload()
        {
            if (Application.isBatchMode && !string.IsNullOrEmpty(Folder) && SessionState.GetString(PhaseKey,"")!="")
                EditorApplication.delayCall+=Verify;
        }

        static void Verify()
        {
            string phase=SessionState.GetString(PhaseKey,"");SessionState.EraseString(PhaseKey);
            try
            {
                WorldSceneSession.Tick();var panel=WorldSceneSession.Panel;
                var expected=JObject.Parse(File.ReadAllText(Path.Combine(Folder,phase+"-before.json")));
                var snapshot=panel.Draft.Snapshot();var prior=(JObject)expected["slot"];
                foreach (var entry in snapshot.Properties())
                    if (!JToken.DeepEquals(prior[entry.Name],entry.Value)) throw new InvalidDataException("重载后草稿字段不同 "+entry.Name);
                var models=ModelState(); if (!JToken.DeepEquals(expected["models"],models)) throw new InvalidDataException("重载后模型不同");
                var camera=(JObject)prior["view"];var pivot=camera["p"].Values<float>().ToArray();var angles=camera["r"].Values<float>().ToArray();
                var view=EditorWindow.GetWindow<SceneView>();
                if (Vector3.Distance(view.pivot,new Vector3(pivot[0],pivot[1],pivot[2]))>.001f || Quaternion.Angle(view.rotation,new Quaternion(angles[0],angles[1],angles[2],angles[3]))>.05f || Mathf.Abs(view.size-(float)camera["zoom"])>.001f || view.orthographic!=(bool)camera["ortho"])
                    throw new InvalidDataException("视角未完整还原");
                string id=panel.Draft.Selected;var origin=RoomFile.At(panel.Draft.Find(id));panel.Draft.Redo();
                if (RoomFile.At(panel.Draft.Find(id))==origin) throw new InvalidDataException("恢复后的重做失效");
                File.AppendAllText(Path.Combine(Folder,"reload.txt"),"PASS "+phase+" 脚本域重载还原草稿、工作路径、选择、工具、视角、历史和 "+models.Count+" 个地形/对象/选中标记；单一预览根，重做有效。\n");
                if (phase=="world") { panel.Draft.Open(Path.Combine(Application.streamingAssetsPath,"World","forest.room.json"),true);panel.Draft.Save(Path.Combine(Folder,"solo.room.json"),false);Prepare("room"); }
                else EditorApplication.Exit(0);
            }
            catch(Exception error){File.AppendAllText(Path.Combine(Folder,"reload.txt"),"FAIL "+phase+" "+error+"\n");Debug.LogException(error);EditorApplication.Exit(1);}
        }
    }
}
