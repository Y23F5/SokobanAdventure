/**
 * @file LayoutVerification.cs
 * @brief WS-01 可复现的工程检查。Build 建立缺少的场景并构建 Windows 包；Verify 从固定样例执行格式、命令和场景预览检查；
 * Expect 将断言及观测写入证据；Rejected 检查错误被明确拒绝；VerifyReturn 读回实际从另一端导出的 B 并与 A 对比全部语义字段。
 */
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;

namespace Sokoban.Editor
{
    public static class LayoutVerification
    {
        static readonly string Output = "Docs/验收证据/WS-01/复测-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "/";
        static string record;
        public static void Build()
        {
            var scenes=new[]{"MainMenu","Game","RuntimeEditor"}.Select(n=>"Assets/Scenes/"+n+".unity").ToArray();
            foreach(string path in scenes)
            {
                if(File.Exists(path)) continue;
                var scene=EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,NewSceneMode.Single);
                if(!path.EndsWith("/Game.unity")) new GameObject("Layout host",typeof(LayoutHost));
                if(!EditorSceneManager.SaveScene(scene,path)) throw new IOException(path);
            }
            EditorBuildSettings.scenes=scenes.Select(p=>new EditorBuildSettingsScene(p,true)).ToArray();
            Verify();
            var result=BuildPipeline.BuildPlayer(scenes,"Builds/WS-01/Player/Sokoban.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
            Expect(result.summary.result==BuildResult.Succeeded && result.summary.totalErrors==0,"Windows x64 build " + result.summary.result+", errors="+result.summary.totalErrors);
        }
        static void Expect(bool ok,string message)
        {
            File.AppendAllText(record,(ok?"PASS ":"FAIL ")+message+Environment.NewLine);
            if(!ok) throw new InvalidOperationException(message);
        }
        static bool Rejected(Action action)
        { try { action(); return false; } catch(InvalidDataException) { return true; } }
        public static void Verify()
        {
            Directory.CreateDirectory(Output); record=Output+"editor-verification.txt";
            File.WriteAllText(record,Application.unityVersion+" | "+DateTimeOffset.Now.ToString("o")+Environment.NewLine);
            var source=Resources.Load<TextAsset>("DefaultRoom").text;
            var d=new RoomDraft(source); var room=d.Room;
            Expect(JToken.DeepEquals(room,JObject.Parse(File.ReadAllText("Docs/关卡样例/WS-01.json"))) && RoomFile.Size(room)==new Vector2Int(7,7) && (string)room["primaryEntranceId"]=="entry" && RoomFile.At(d.Find("box-a"))==new Vector2Int(3,2) && RoomFile.At(d.Find("target-a"))==new Vector2Int(4,2) && RoomFile.At(d.Find("spawn"))==new Vector2Int(2,2) && room["connections"]==null,"FLOW01-001 built-in equals design fixture; size 7x7, origin zero, stable spawn, target, box, recovery, doors and theme");
            var window=RoomWindow.Open(); var panel=window.Panel; d=panel.Draft;
            d.Open("Docs/关卡样例/WS-01.json",true);
            d.Tool=RoomTool.Target; panel.Click(new Vector2Int(4,4)); d.Tool=RoomTool.Box; panel.Click(new Vector2Int(2,4)); string id=d.Selected;
            d.Tool=RoomTool.Select; panel.Click(new Vector2Int(2,4)); Expect(d.Selected==id,"Editor selection "+id);
            foreach(var bad in new[]{new Vector2Int(0,1),new Vector2Int(1,3),new Vector2Int(3,2),new Vector2Int(4,2),new Vector2Int(-1,0)})
            { var before=d.Room; Expect(Rejected(()=>d.Place(true,bad)) && before==d.Room,"Reject "+bad+" without changing draft"); }
            Expect(Rejected(()=>d.Place(false,new Vector2Int(3,2))) && Rejected(()=>d.Move(new Vector2Int(4,2))),"Reject target on box and move onto target");
            var overlap=(JObject)d.Room.DeepClone();
            overlap["targets"][0]["cell"]=new JArray(3,2);
            Expect(Rejected(()=>RoomFile.Parse(overlap.ToString())),"Reject overlapped room on import");
            var baseline=d.Room; d.Move(new Vector2Int(3,4)); d.Undo(); Expect(JToken.DeepEquals(baseline,d.Room),"Single undo restores design"); d.Redo(); d.Undo();
            var projection=new RoomPreview(); projection.Sync(d); var box=GameObject.Find(id); var position=box.transform.localPosition;
            box.transform.localPosition=Vector3.one*60; box.transform.localScale=Vector3.one*4; box.transform.localRotation=Quaternion.Euler(90,25,5); projection.Sync(d);
            Expect(box.transform.localPosition==position && box.transform.localRotation==Quaternion.identity && box.transform.localScale!=Vector3.one*4,"Preview restores external position/rotation/scale from data"); projection.Clear();
            d.Save(Output+"A.json",false); window.Close(); window=RoomWindow.Open(); d=window.Panel.Draft; d.Open(Output+"A.json",false);
            Expect(d.Room["boxes"].Count()==2 && d.Room["targets"].Count()==2 && RoomFile.At(d.Find(id))==new Vector2Int(2,4),"EditorWindow reopen A with two boxes and two targets");
            var legacy=(JObject)room.DeepClone(); legacy.Remove("kind"); legacy.Remove("primaryEntranceId"); legacy["formatVersion"]=1; legacy["connections"]=new JArray();legacy["terrainRowsNorthToSouth"][3]="<.....>";
            File.WriteAllText(Output+"legacy.json",legacy.ToString()); d.Open(Output+"legacy.json",true); d.Save(Output+"upgraded.json",true);
            Expect(JToken.DeepEquals(d.Room,room) && JObject.Parse(File.ReadAllText(Output+"legacy.json"))["formatVersion"].Value<int>()==1,"Legacy upgrade preserves all fields and original");
            legacy["formatVersion"]=99; File.WriteAllText(Output+"unknown.json",legacy.ToString());var previous=d.Room;
            Expect(Rejected(()=>d.Open(Output+"unknown.json",true)) && previous==d.Room,"Unknown format rejected, draft retained"); window.Close();
        }
        public static void VerifyReturn()
        {
            var folder = Environment.GetEnvironmentVariable("SOKOBAN_EXCHANGE_DIR") ?? "Docs/验收证据/WS-01";
            record=Path.Combine(folder,"return-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt"); File.WriteAllText(record,Application.unityVersion+" | "+DateTimeOffset.Now.ToString("o")+Environment.NewLine);
            var window=RoomWindow.Open(); var d=window.Panel.Draft; d.Open(Path.Combine(folder,"B.json"),true); var b=d.Room; d.Open(Path.Combine(folder,"A.json"),false);
            var expected=(JObject)d.Room.DeepClone(); var box=expected["boxes"].Single(x=>RoomFile.At(x)==new Vector2Int(2,4));
            box["cell"]=new JArray(3,4);box["recoveryCell"]=new JArray(3,4);
            Expect(JToken.DeepEquals(expected,b),"Unity editor reimport B and reopen A: only cell and recoveryCell of "+box["id"]+" differ, (2,4) -> (3,4)");window.Close();
        }
    }
}
