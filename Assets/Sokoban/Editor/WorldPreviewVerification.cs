/**
 * @file WorldPreviewVerification.cs
 * @brief Run 在隔离的 Unity 编辑态打开实际场景，比对模型层级、房间平移、独立查看和重开，数据全部取自 Core 类型。
 * Require(condition, detail)：把断言结果写入证据文件，失败抛 InvalidDataException。
 * Root()：找到唯一的活动预览根。
 * TileName(room, cell)：按地形分类给出预览节点前缀，门下仍有地板，空格返回空。
 * Links(world)：把世界的全部连接（ID、方式、两端、allowReturn）拼成可比较的字符串。
 * CheckRoom(model, origin)：核对房间原点、每个地形格与对象的模型位置，以及子节点总数。
 * CheckModel(instance, kind, rotation, expectedSurface)：核对模型构件、材质及朝向。
 * CheckFactoryModels(draft)：根据工厂草稿逐一比较显示模型与资源，在放置、移动及独立打开后复用。
 * CheckFactoryRoundTrip(panel, folder)：通过布局面板移动箱子、撤销重做、保存并重新打开工作文件，比较整房编码。
 * Run()：批处理入口，环境变量 SOKOBAN_EXCHANGE_DIR 给证据目录，依次验证三次直开、独立查看、返回、整房重放与独立工厂往返。
 */
using System;
using System.IO;
using System.Linq;
using Sokoban.Core;
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

        static string TileName(Room room, Cell cell) => room.TerrainAt(cell).CategoryId switch
        {
            TerrainCategories.Floor => "Floor",
            TerrainCategories.Door => "Floor",
            TerrainCategories.Wall => "Wall",
            TerrainCategories.Openable => "Vine",
            _ => null
        };

        static string Links(WorldDocument world) =>
            string.Join(";", world.Connections.Select(c => c.Id + "|" + c.Mode + "|" + c.From.LevelId + "." + c.From.DoorId + ">" + c.To.LevelId + "." + c.To.DoorId + "|" + c.AllowReturn));

        static void CheckRoom(RoomDraftModel model, Cell origin)
        {
            var room = model.Room;
            var group = Root().Find(room.Id);
            Require(group && group.position == new Vector3(origin.X,0,origin.Y), room.Id + " 房间原点 " + origin);
            int tiles = 0;
            for (int y=0;y<room.Height;y++) for (int x=0;x<room.Width;x++)
            {
                var cell=new Cell(x,y);
                string name=TileName(room,cell);
                if (name==null) continue;
                var tile=group.Find(name+" "+cell);
                if (!tile || tile.localPosition!=new Vector3(x,0,y) || !tile.GetComponentInChildren<Renderer>(true))
                    throw new InvalidDataException("格子模型错误 "+cell);
                tiles++;
            }
            foreach (var obj in room.Objects)
            {
                var part=group.Find(obj.Id);
                if (!part || part.localPosition!=new Vector3(obj.Cell.X,0,obj.Cell.Y) || !part.GetComponentInChildren<Renderer>())
                    throw new InvalidDataException("对象模型错误 "+obj.Id);
            }
            Require(model.Doors.All(door =>
            {
                var frame = group.Find(door.Id);
                return frame && frame.localPosition == new Vector3(door.Cell.X, 0, door.Cell.Y);
            }), room.Id + " 门框与门下地板分别存在");
            Require(group.Find("地基") && group.Find("外围地绘") && group.childCount == 2+tiles+model.Doors.Count+room.Objects.Count+(group.Find("Selection")?1:0),
                room.Id + " 各格和对象均唯一，模型数量 " + group.childCount);
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
            var room = draft.Room;
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                {
                    var cell = new Cell(x, y);
                    string kind = TileName(room, cell);
                    if (kind != "Floor" && kind != "Wall") continue;
                    string prefab = kind == "Floor" ? "Diorama/factoryTile0" : "FactoryWall";
                    CheckModel(group.Find(kind + " " + cell), prefab, Quaternion.identity);
                }
            foreach (var door in draft.Model.Doors)
                CheckModel(group.Find(door.Id), "Door", Quaternion.LookRotation(new Vector3(-door.Outward.X, 0, -door.Outward.Y)));
            foreach (var box in room.ObjectsOf(ObjectCategories.Box)) CheckModel(group.Find(box.Id), RoomPreview.BoxModel("factory"), Quaternion.identity);
            foreach (var target in room.ObjectsOf(ObjectCategories.Target)) CheckModel(group.Find(target.Id), "FactoryTarget", Quaternion.identity);
            var spawn = room.ObjectsOf(ObjectCategories.Spawn).Single();
            CheckModel(group.Find(spawn.Id), "Spawn", Quaternion.Euler(0, 180 + spawn.Rotation * 90, 0));
            CheckRoom(draft.Model, draft.Origin);
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
            var original = RoomCodec.Encode(draft.Model);
            draft.Tool = RoomTool.Select; panel.ClickWorld(start);
            draft.Tool = RoomTool.Move; panel.ClickWorld(end);
            Require(draft.Selected == "box-2" && draft.Find("box-2").Cell == new Cell(end.x, end.y), "选中 box-2 并向左移一格");
            var moved = RoomCodec.Encode(draft.Model);
            draft.Undo(); Require(original == RoomCodec.Encode(draft.Model), "撤销一步恢复原房间");
            draft.Redo(); Require(moved == RoomCodec.Encode(draft.Model), "重做恢复新房间");
            panel.ChooseFile("保存");
            Require(moved == File.ReadAllText(path), "工作文件逐字等于已修改房间的编码");
            panel.ChooseFile("打开"); panel.Filename = path; panel.SubmitFile(); WorldSceneSession.Tick();
            Require(!panel.Modal && moved == RoomCodec.Encode(draft.Model) && draft.Find("box-2").Cell == new Cell(end.x, end.y), "重开后稳定 ID 与位置一致");
            CheckRoom(draft.Model, new Cell(0, 0));
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
                    foreach(var placement in draft.World.Rooms)
                        CheckRoom(draft.Catalog[placement.LevelId],placement.Origin);
                    var root=Root(); RoomLayoutWindow.Open().Close();WorldSceneSession.Tick();
                    Require(root==Root(),"打开并关闭工具窗口保留原预览实例");
                }
                var panel=WorldSceneSession.Panel;var state=panel.Draft;
                state.UseRoom("factory");WorldSceneSession.Tick();
                var view=EditorWindow.GetWindow<SceneView>();view.Focus();
                WorldSceneSession.FocusRoom();var pivot=view.pivot;var rotation=view.rotation;float size=view.size;
                WorldSceneSession.Solo();WorldSceneSession.Tick();Require(Root().childCount==1,"单独查看只生成选中房间");
                CheckRoom(state.Model,state.Origin);
                var originalRoom = RoomCodec.Encode(state.Model);
                var boxCell = state.Find("box-2").Cell;
                state.Select(boxCell);
                state.Move(boxCell + Cell.Left);
                WorldSceneSession.ReturnToWorld();WorldSceneSession.Tick();
                Require(Root().childCount==3 && view.pivot==pivot && view.rotation==rotation && Mathf.Approximately(view.size,size),"返回还原整图、观察中心、朝向及缩放");
                CheckRoom(state.Model, state.Origin);
                state.Undo();
                Require(originalRoom == RoomCodec.Encode(state.Model), "退出单房后的第一步撤销还原房内纸箱位置");
                var history = state.Snapshot();
                WorldSceneSession.Solo();
                WorldSceneSession.FrameWorld();
                WorldSceneSession.Tick();
                Require(!WorldSceneSession.IsSolo && Root().childCount == 3 && view.size > size && history == state.Snapshot(), "从单房显示整图可见三房，取景扩大且未修改文档或历史");
                var original=RoomCodec.Encode(state.Model);
                var docked = state.Origin;
                var savedLinks = Links(state.World);
                state.RemoveRoom();
                panel.StartRoomPlacement();
                var detached = state.Snapshot();
                var away = new Vector2Int(40, 10);
                Require(panel.Candidate(away, out var detachedWarning) == Color.yellow && detachedWarning.Contains("factory") && detachedWarning.Contains("直连"), "从目录重新放置，移远已连接的工厂应显示黄色直连警告");
                Require(panel.Candidate(new Vector2Int(docked.X, docked.Y), out _) == Color.green && detached == state.Snapshot(), "重新对接的预览为绿色，检查预览不改变草稿或历史");
                panel.CancelTool();
                Require(state.Placement == null && detached == state.Snapshot(), "取消整房重放保留未放置状态");
                panel.StartRoomPlacement(); panel.ConfirmRoom(away);
                WorldSceneSession.Tick(); CheckFactoryModels(state);
                Require(state.Origin == new Cell(away.x, away.y) && savedLinks == Links(state.World), "黄色确认保留端点、连接模式和 allowReturn");
                panel.StartRoomPlacement(); panel.ConfirmRoom(new Vector2Int(docked.X, docked.Y));
                WorldSceneSession.Tick(); CheckFactoryModels(state);
                Require(state.Origin == docked && !WorldLayout.ConnectionIssues(state.World,state.Catalog).Any() && savedLinks == Links(state.World), "回到原点后两扇直连门重新对接并消除警告");
                var snapshot=state.Snapshot();
                panel.StartRoomPlacement();var candidate=new Vector2Int(40,10);
                var max = state.World.Max;
                var eastEdge = new Vector2Int(max.X, 0);
                Require(panel.Candidate(eastEdge, out var boundary) == Color.red && boundary.Contains($"({(long)max.X + 1}, 0)"), "右缘越界指出第一个越界格，而原点仍在地图内");
                panel.ConfirmRoom(eastEdge);
                Require(panel.PlacingRoom && snapshot == state.Snapshot(), "红色候选拒绝确认并保留原位和历史");
                Require(panel.Candidate(candidate,out var warning)==Color.yellow && warning.Contains("直连"),"移远已对接房间给出黄色提示");
                var ghost=new RoomPreview();ghost.ShowPlacement(state,candidate,Color.yellow);
                var instance=GameObject.Find("房间放置预览");
                Require(instance && instance.transform.Find("factory") && instance.GetComponentsInChildren<LineRenderer>().Length==2,"虚影含整房模型、原位和候选轮廓");
                ghost.HidePlacement();panel.CancelTool();Require(!GameObject.Find("房间放置预览") && snapshot==state.Snapshot(),"取消清理虚影，不改设计或历史");
                panel.StartRoomPlacement();panel.ConfirmRoom(candidate);WorldSceneSession.Tick();CheckRoom(state.Model,new Cell(candidate.x,candidate.y));
                state.Undo();WorldSceneSession.Tick();Require(original==RoomCodec.Encode(state.Model) && state.Origin!=new Cell(candidate.x,candidate.y),"一步撤销还原原点与对象");
                state.Open(Path.Combine(Application.streamingAssetsPath,"World","forest.room.json"),false);WorldSceneSession.Tick();
                Require(Root().childCount==1,"独立房间只生成一份");CheckRoom(state.Model,new Cell(0,0));
                CheckFactoryRoundTrip(panel, folder);
            }
        }
    }
}
