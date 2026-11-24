/**
 * @file WS06PBlueprintVerification.cs
 * @brief Run 从编辑图纸制作三房，经过面板草稿、撤销重做、独立保存与整图重开后核对布局和出口解法。
 */
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class WS06PBlueprintVerification
    {
        public static void Run()
        {
            string output = Environment.GetEnvironmentVariable("SOKOBAN_BLUEPRINT_OUTPUT");
            Directory.CreateDirectory(output);
            string world = Path.Combine(Application.streamingAssetsPath,"World");
            string manifest = Path.Combine(output,"main.world.json");
            File.Copy(Path.Combine(world,"main.world.json"),manifest,false);
            foreach (var id in new[]{"warehouse","factory","forest"})
            {
                string path = Path.Combine(output,id + ".room.json");
                var source = RoomBlueprint.Read(Path.Combine(Application.dataPath,"Sokoban/Editor/Blueprints",id + ".txt"));
                var panel = new LayoutPanel("production-" + id, output);
                panel.OpenBlueprint(source);
                var draft = panel.Draft;
                RoomFile.Check((string)draft.Room["recoveryPresentation"] == id,"图纸缺少恢复演出绑定");
                panel.CycleRecovery();
                RoomFile.Check((string)draft.Room["recoveryPresentation"] != id,"不能切换恢复演出");
                draft.Undo();
                var built = draft.Room.DeepClone();
                draft.Undo();
                RoomFile.Check(!JToken.DeepEquals(draft.Room,built),"撤销无效");
                draft.Redo();
                draft.Save(path,false);
                draft.Open(path,false);
                RoomFile.Check(JToken.DeepEquals(built,draft.Room),"文件往返不一致");
                var original = JObject.Parse(File.ReadAllText(Path.Combine(world,id + ".room.json")));
                foreach (string field in new[]{"levelId","name","themeId","size","terrainRowsNorthToSouth","spawn","doors","boxes","targets","primaryEntranceId"})
                    RoomFile.Check(JToken.DeepEquals(original[field],draft.Room[field]), id + " 与图纸基线不一致 " + field);
                var solved = RoomSolution.Check(draft.Room,RoomFile.At(draft.Room["spawn"]),destination:RoomFile.At(draft.Room["doors"].Last()));
                RoomFile.Check(solved.Status == RoomSolution.Outcome.Solved, solved.Detail);
                Debug.Log("BLUEPRINT PASS " + id + " " + solved.Detail);
            }
            var catalog = new JObject();
            WorldLayout.Read(manifest,catalog);
            RoomFile.Check(catalog.Count==3,"正式世界房间缺失");
            Debug.Log("BLUEPRINT WORLD PASS " + manifest);
        }
    }
}
