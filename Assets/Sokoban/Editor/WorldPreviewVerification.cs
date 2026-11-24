/**
 * @file WorldPreviewVerification.cs
 * @brief Run 在隔离的 Unity 编辑态打开实际场景，比对模型层级、房间平移、独立查看和重开。
 * Root 找到唯一的活动预览根；CheckRoom 核对格子和对象的模型位置；Require 保存断言结果。
 * CheckFactoryRoundTrip 通过布局面板移动箱子、撤销重做并重新打开工作文件；CheckModel 核对模型构件、材质及朝向。
 * CheckFactoryModels 根据工厂草稿逐一比较显示模型与资源，在放置、移动及独立打开后复用。
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
    public static class WorldPreviewVerification
    {
        static StreamWriter record;

        static void Require(bool condition, string detail)
        {
            record.WriteLine((condition ? "PASS " : "FAIL ") + detail);
            if (!condition) throw new InvalidDataException(detail);
        }

        static Transform Root()
        {
            return SceneManager.GetActiveScene().GetRootGameObjects()
                .Single(go => go.activeSelf && go.name.StartsWith("Room • ",StringComparison.Ordinal)).transform;
        }

        static void CheckRoom(JObject room, Vector2Int origin)
        {
            var group = Root().Find((string)room["levelId"]);
            var size = RoomFile.Size(room);
            Require(group && group.position == new Vector3(origin.x,0,origin.y), room["levelId"] + " 房间原点 " + origin);
            for (int y=0;y<size.y;y++) for (int x=0;x<size.x;x++)
            {
                var cell=new Vector2Int(x,y);
                string kind=RoomFile.Floor(room,cell)?"Floor":"Wall";
                var tile=group.Find(kind+" "+cell);
                if (!tile || tile.localPosition!=new Vector3(x,0,y) || !tile.GetComponentInChildren<Renderer>())
                    throw new InvalidDataException("格子模型错误 "+cell);
            }
            foreach (var obj in RoomFile.Objects(room))
            {
                var part=group.Find((string)obj["id"]);var cell=RoomFile.At(obj);
                if (!part || part.localPosition!=new Vector3(cell.x,0,cell.y) || !part.GetComponentInChildren<Renderer>())
                    throw new InvalidDataException("对象模型错误 "+obj["id"]);
            }
            Require(group.Find("地基") && group.Find("主题模型") && group.childCount == 2+size.x*size.y+RoomFile.Objects(room).Count()+(group.Find("Selection")?1:0),
                room["levelId"] + " 各格和对象均唯一，模型数量 " + group.childCount);
        }

        static void CheckModel(Transform instance, string kind, Quaternion rotation, Material expectedSurface=null)
        {
            var prefab = Resources.Load<GameObject>(kind.Contains("/") ? kind : "LayoutModels/" + kind);
            var expected = prefab.GetComponentsInChildren<MeshFilter>(true);
            var actual = instance.GetComponentsInChildren<MeshFilter>(true);
            if (actual.Length != expected.Length || actual.Length == 0 || Quaternion.Angle(instance.localRotation, rotation) > .01f)
                throw new InvalidDataException(instance.name + " 构件数量或根朝向不符");
            for (int index = 0; index < expected.Length; index++)
            {
                var source = expected[index];
                var part = actual[index];
                if (source.sharedMesh != part.sharedMesh || !(expectedSurface ? new[]{expectedSurface} : source.GetComponent<Renderer>().sharedMaterials).SequenceEqual(part.GetComponent<Renderer>().sharedMaterials)
                    || source.transform.localScale != part.transform.localScale || source.transform != prefab.transform && (source.name != part.name
                    || source.transform.localPosition != part.transform.localPosition || Quaternion.Angle(source.transform.localRotation, part.transform.localRotation) > .01f))
                    throw new InvalidDataException(instance.name + " 构件不符：" + source.name);
            }
        }

        static void CheckFactoryModels(RoomDraft draft)
        {
            var group = Root().Find("factory");
            var size = RoomFile.Size(draft.Room);
            for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                {
                    var cell = new Vector2Int(x, y);
                    var kind = RoomFile.Floor(draft.Room, cell) ? "Floor" : "Wall";
                    CheckModel(group.Find(kind + " " + cell), kind, Quaternion.identity,kind=="Floor"?Resources.Load<Material>("LayoutModels/Stone"):null);
                }
            foreach (var door in draft.Room["doors"])
            {
                var inward = RoomFile.Inward(door);
                CheckModel(group.Find((string)door["id"]), "Door", Quaternion.LookRotation(new Vector3(inward.x, 0, inward.y)));
            }
            foreach (var box in draft.Room["boxes"]) CheckModel(group.Find((string)box["id"]), RoomPreview.BoxModel("factory"), Quaternion.identity);
            foreach (var target in draft.Room["targets"]) CheckModel(group.Find((string)target["id"]), "Target", Quaternion.identity);
            CheckModel(group.Find((string)draft.Room["spawn"]["id"]), "Spawn", Quaternion.identity);
            CheckRoom(draft.Room, draft.Origin);
            Require(!GameObject.Find("房间放置预览"), "工厂 " + draft.Origin + " 的格子及物件与 Prefab 的网格、材质、部件和朝向一致，无残留虚影");
        }

        static void CheckFactoryRoundTrip(LayoutPanel panel, string folder)
        {
            var source = Path.Combine(Application.streamingAssetsPath, "World", "factory.room.json");
            var path = Path.GetFullPath(Path.Combine(folder, "工厂接口往返.room.json"));
            File.Copy(source, path, true);
            panel.ChooseFile("打开"); panel.Filename = path; panel.SubmitFile();
            if (panel.Confirming) panel.DiscardAndContinue();
            WorldSceneSession.Tick();
            var draft = panel.Draft;
            Require(draft.World == null && draft.WorkPath == path && Root().childCount == 1, "布局打开独立工厂并按零原点生成单一预览");
            CheckFactoryModels(draft);
            var start = new Vector2Int(4, 2);
            var end = start + Vector2Int.left;
            var original = draft.Room.DeepClone();
            draft.Tool = RoomTool.Select; panel.ClickWorld(start);
            draft.Tool = RoomTool.Move; panel.ClickWorld(end);
            Require(draft.Selected == "box-2" && RoomFile.At(draft.Find("box-2")) == end, "选中 box-2 并向左移一格");
            var moved = draft.Room.DeepClone();
            draft.Undo(); Require(JToken.DeepEquals(original, draft.Room), "撤销一步恢复原房间");
            draft.Redo(); Require(JToken.DeepEquals(moved, draft.Room), "重做恢复新房间");
            panel.ChooseFile("保存");
            Require(JToken.DeepEquals(moved, JObject.Parse(File.ReadAllText(path))), "工作文件逐字段等于已修改房间");
            panel.ChooseFile("打开"); panel.Filename = path; panel.SubmitFile(); WorldSceneSession.Tick();
            Require(!panel.Modal && JToken.DeepEquals(moved, draft.Room) && RoomFile.Cell(draft.Find("box-2")["recoveryCell"]) == end, "重开后稳定 ID、位置及恢复格一致");
            CheckRoom(draft.Room, Vector2Int.zero);
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("通过隔离工程批处理运行，保护当前编辑现场");
            string folder=Environment.GetEnvironmentVariable("SOKOBAN_EXCHANGE_DIR");
            using(record=new StreamWriter(Path.Combine(folder,"编辑态模型.txt")){AutoFlush=true})
            {
                for(int open=0;open<3;open++)
                {
                    EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
                    EditorSceneManager.OpenScene(WorldSceneSession.GamePath);
                    WorldSceneSession.Tick();WorldSceneSession.Tick();
                    var draft=WorldSceneSession.Panel.Draft;
                    Require(Root().childCount==3,"第 "+(open+1)+" 次直接打开 Game 生成一份三房整图");
                    foreach(var placement in draft.World["rooms"])
                        CheckRoom((JObject)draft.Catalog[(string)placement["levelId"]],RoomFile.Cell(placement["origin"]));
                    var root=Root(); RoomWindow.Open().Close();WorldSceneSession.Tick();
                    Require(root==Root(),"打开并关闭工具窗口保留原预览实例");
                }
                var panel=WorldSceneSession.Panel;var state=panel.Draft;
                state.UseRoom("factory");WorldSceneSession.Tick();
                var view=EditorWindow.GetWindow<SceneView>();view.Focus();
                WorldSceneSession.FocusRoom();var pivot=view.pivot;var rotation=view.rotation;float size=view.size;
                WorldSceneSession.Solo();WorldSceneSession.Tick();Require(Root().childCount==1,"单独查看只生成选中房间");
                CheckRoom(state.Room,state.Origin);
                var originalRoom = state.Room.DeepClone();
                var boxCell = RoomFile.At(state.Find("box-2"));
                state.Select(boxCell);
                state.Move(boxCell + Vector2Int.left);
                WorldSceneSession.ReturnToWorld();WorldSceneSession.Tick();
                Require(Root().childCount==3 && view.pivot==pivot && view.rotation==rotation && Mathf.Approximately(view.size,size),"返回还原整图、观察中心、朝向及缩放");
                CheckRoom(state.Room, state.Origin);
                state.Undo();
                Require(JToken.DeepEquals(originalRoom, state.Room), "退出单房后的第一步撤销还原房内纸箱和恢复格");
                var history = state.Snapshot();
                WorldSceneSession.Solo();
                WorldSceneSession.FrameWorld();
                WorldSceneSession.Tick();
                Require(!WorldSceneSession.IsSolo && Root().childCount == 3 && view.size > size && JToken.DeepEquals(history, state.Snapshot()), "从单房显示整图可见三房，取景扩大且未修改文档或历史");
                var original=state.Room.DeepClone();
                var docked = state.Origin;
                var savedLinks = state.World["connections"].DeepClone();
                state.RemoveRoom();
                panel.StartRoomPlacement();
                var detached = state.Snapshot();
                var away = new Vector2Int(40, 10);
                Require(panel.Candidate(away, out var detachedWarning) == Color.yellow && detachedWarning.Contains("factory") && detachedWarning.Contains("直连"), "从目录重新放置，移远已连接的工厂应显示黄色直连警告");
                Require(panel.Candidate(docked, out _) == Color.green && JToken.DeepEquals(detached, state.Snapshot()), "重新对接的预览为绿色，检查预览不改变草稿或历史");
                panel.CancelTool();
                Require(state.Placement == null && JToken.DeepEquals(detached, state.Snapshot()), "取消整房重放保留未放置状态");
                panel.StartRoomPlacement(); panel.ConfirmRoom(away);
                WorldSceneSession.Tick(); CheckFactoryModels(state);
                Require(state.Origin == away && JToken.DeepEquals(savedLinks,state.World["connections"]), "黄色确认保留端点、连接模式和 allowReturn");
                panel.StartRoomPlacement(); panel.ConfirmRoom(docked);
                WorldSceneSession.Tick(); CheckFactoryModels(state);
                Require(state.Origin == docked && !WorldLayout.ConnectionIssues(state.World,state.Catalog).Any() && JToken.DeepEquals(savedLinks,state.World["connections"]), "回到原点后两扇直连门重新对接并消除警告");
                var snapshot=state.Snapshot();
                panel.StartRoomPlacement();var candidate=new Vector2Int(40,10);
                var max = RoomFile.Cell(state.World["bounds"]["max"]);
                var eastEdge = new Vector2Int(max.x, 0);
                Require(panel.Candidate(eastEdge, out var boundary) == Color.red && boundary.Contains($"({(long)max.x + 1}, 0)"), "右缘越界指出第一个越界格，而原点仍在地图内");
                panel.ConfirmRoom(eastEdge);
                Require(panel.PlacingRoom && JToken.DeepEquals(snapshot, state.Snapshot()), "红色候选拒绝确认并保留原位和历史");
                Require(panel.Candidate(candidate,out var warning)==Color.yellow && warning.Contains("直连"),"移远已对接房间给出黄色提示");
                var ghost=new RoomPreview();ghost.ShowPlacement(state,candidate,Color.yellow);
                var instance=GameObject.Find("房间放置预览");
                Require(instance && instance.transform.Find("factory") && instance.GetComponentsInChildren<LineRenderer>().Length==2,"虚影含整房模型、原位和候选轮廓");
                ghost.HidePlacement();panel.CancelTool();Require(!GameObject.Find("房间放置预览") && JToken.DeepEquals(snapshot,state.Snapshot()),"取消清理虚影，不改设计或历史");
                panel.StartRoomPlacement();panel.ConfirmRoom(candidate);WorldSceneSession.Tick();CheckRoom(state.Room,candidate);
                state.Undo();WorldSceneSession.Tick();Require(JToken.DeepEquals(original,state.Room) && state.Origin!=candidate,"一步撤销还原原点与对象");
                state.Open(Path.Combine(Application.streamingAssetsPath,"World","forest.room.json"),false);WorldSceneSession.Tick();
                Require(Root().childCount==1,"独立房间只生成一份");CheckRoom(state.Room,Vector2Int.zero);
                CheckFactoryRoundTrip(panel, folder);
            }
        }
    }
}
