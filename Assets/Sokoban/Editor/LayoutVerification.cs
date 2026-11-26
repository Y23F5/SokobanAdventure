/**
 * @file LayoutVerification.cs
 * @brief WS-01 可复现的工程检查，数据全部读写 Core 类型。Build 建立缺少的场景并构建 Windows 包；Verify 从固定样例执行格式、命令和场景预览检查；
 * Expect 将断言及观测写入证据；Rejected 检查错误被明确拒绝；VerifyReturn 读回实际从另一端导出的 B 并与 A 对比全部语义字段。
 * Build()：补建三个运行场景、写入构建设置、先跑 Verify 再构建 Windows x64 包。
 * Expect(ok, message)：把 PASS 或 FAIL 追加到证据文件，失败时抛 InvalidOperationException。
 * Rejected(action)：动作抛 InvalidDataException 或 FormatException 视为被明确拒绝。
 * Verify()：校验默认房间与设计样例、编辑选择、非法放置整笔拒绝且编码不变、重叠房间导入被拒、单次撤销、预览回写、保存后重开。
 * VerifyReturn()：读回另一端导出的 B.json 与 A.json，只允许指定箱子的格子不同。
 * Signature(model)：把尺寸、地形分类网格、门与全部对象（ID、类别、格）拼成可比较的字符串，objectOverride 可替换指定对象的格。
 */
using System;
using System.IO;
using System.Linq;
using System.Text;
using Sokoban.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
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
        { try { action(); return false; } catch(InvalidDataException) { return true; } catch(FormatException) { return true; } }
        static string Signature(RoomDraftModel model, string overrideId = null, Cell overrideCell = default)
        {
            var room = model.Room;
            var text = new StringBuilder(room.Width + "x" + room.Height + "|");
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                    text.Append(room.TerrainAt(new Cell(x, y)).CategoryId).Append(',');
            text.Append('|').Append(string.Join(";", model.Doors.OrderBy(d => d.Id, StringComparer.Ordinal).Select(d => d.Id + ":" + d.Cell + ":" + d.Outward + ":" + d.Role)));
            text.Append('|').Append(model.PrimaryEntranceId).Append('|');
            text.Append(string.Join(";", room.Objects.OrderBy(o => o.Id, StringComparer.Ordinal).Select(o => o.Id + ":" + o.CategoryId + ":" + (o.Id == overrideId ? overrideCell : o.Cell))));
            return text.ToString();
        }
        public static void Verify()
        {
            Directory.CreateDirectory(Output); record=Output+"editor-verification.txt";
            File.WriteAllText(record,Application.unityVersion+" | "+DateTimeOffset.Now.ToString("o")+Environment.NewLine);
            var fixture=RoomFile.Load("Docs/关卡样例/WS-01.json");
            var built=RoomCodec.Decode(Resources.Load<TextAsset>("DefaultRoom").text);
            Expect(built.Room.Width==7 && built.Room.Height==7 && built.PrimaryEntranceId=="entry" && built.Room.ObjectsOf(ObjectCategories.Spawn).Single().Cell==new Cell(2,2) && built.Doors.Select(x=>x.Id).OrderBy(x=>x).SequenceEqual(new[]{"entry","exit"}),"FLOW01-001 built-in default room: 7x7, stable spawn (2,2), primary entrance and entry/exit doors");
            Expect(fixture.Room.Width==7 && fixture.Room.Height==7 && fixture.PrimaryEntranceId=="entry" && fixture.Room.Objects.Single(o=>o.Id=="box-a").Cell==new Cell(3,2) && fixture.Room.Objects.Single(o=>o.Id=="target-a").Cell==new Cell(4,2) && fixture.Room.Objects.Single(o=>o.Id=="spawn").Cell==new Cell(2,2),"FLOW01-001 design fixture loads: size 7x7, origin zero, stable spawn, box, target, doors and primary entrance");
            var window = RoomLayoutWindow.Open();
            window.CreateGUI();
            var view = window.rootVisualElement;
            Expect(view.Q<TwoPaneSplitView>("palette-split") != null && view.Q<TwoPaneSplitView>("inspector-split") != null
                && view.Q("files") != null && view.Q<ScrollView>("issues") != null, "Room layout instantiates the UXML panes, file toolbar and issue list");
            var panel = window.Panel;
            var d = panel.Draft;
            d.Open("Docs/关卡样例/WS-01.json",true);
            d.Tool=RoomTool.Target; panel.Click(new Cell(4,4)); d.Tool=RoomTool.Box; panel.Click(new Cell(2,4)); string id=d.Selected;
            d.Tool=RoomTool.Select; panel.Click(new Cell(2,4)); Expect(d.Selected==id,"Editor selection "+id);
            foreach(var bad in new[]{new Cell(0,1),new Cell(1,3),new Cell(3,2),new Cell(4,2),new Cell(-1,0)})
            { var before=RoomCodec.Encode(d.Model); Expect(Rejected(()=>d.Place(true,bad)) && before==RoomCodec.Encode(d.Model),"Reject "+bad+" without changing draft"); }
            Expect(Rejected(()=>d.Place(false,new Cell(3,2))) && Rejected(()=>d.Move(new Cell(4,2))),"Reject target on box and move onto target");
            var overlapText=File.ReadAllText("Docs/关卡样例/WS-01.json").Replace("{\"id\":\"target-a\",\"category\":\"target\",\"x\":4,\"y\":2}","{\"id\":\"target-a\",\"category\":\"target\",\"x\":3,\"y\":2}");
            Expect(overlapText!=File.ReadAllText("Docs/关卡样例/WS-01.json") && Rejected(()=>RoomCodec.Decode(overlapText)),"Reject overlapped room on import");
            var baseline=RoomCodec.Encode(d.Model); d.Move(new Cell(3,4)); Expect(RoomCodec.Encode(d.Model)!=baseline,"Move changes the draft"); d.Undo(); Expect(baseline==RoomCodec.Encode(d.Model),"Single undo restores design"); d.Redo(); d.Undo();
            var currentRoots = SceneManager.GetActiveScene().GetRootGameObjects();
            var projection = new RoomPreview();
            try
            {
                projection.Sync(d);
                var projectionRoot = SceneManager.GetActiveScene().GetRootGameObjects().Except(currentRoots).Single();
                var box = projectionRoot.transform.Find(d.ActiveId).Find(id);
                var position = box.localPosition;
                var rotation = box.localRotation;
                var scale = box.localScale;
                box.localPosition = Vector3.one * 60;
                box.localRotation = Quaternion.Euler(90, 25, 5);
                box.localScale = Vector3.one * 4;
                projection.Sync(d);
                Expect(box.localPosition == position && box.localRotation == rotation && box.localScale == scale,
                    "Preview restores external position/rotation/scale from data");
            }
            finally { projection.Clear(); }
            d.Save(Output+"A.json",false); window.Close(); window=RoomLayoutWindow.Open(); d=window.Panel.Draft; d.Open(Output+"A.json",false);
            Expect(d.Room.ObjectsOf(ObjectCategories.Box).Count()==2 && d.Room.ObjectsOf(ObjectCategories.Target).Count()==2 && d.Find(id).Cell==new Cell(2,4),"EditorWindow reopen A with two boxes and two targets");
            window.Close();
        }
        public static void VerifyReturn()
        {
            var folder = Environment.GetEnvironmentVariable("SOKOBAN_EXCHANGE_DIR") ?? "Docs/验收证据/WS-01";
            record=Path.Combine(folder,"return-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt"); File.WriteAllText(record,Application.unityVersion+" | "+DateTimeOffset.Now.ToString("o")+Environment.NewLine);
            var window=RoomLayoutWindow.Open(); var d=window.Panel.Draft; d.Open(Path.Combine(folder,"B.json"),true); var b=d.Model; d.Open(Path.Combine(folder,"A.json"),false);
            var moved=d.Room.ObjectsOf(ObjectCategories.Box).Single(x=>x.Cell==new Cell(2,4));
            Expect(Signature(d.Model,moved.Id,new Cell(3,4))==Signature(b),"Unity editor reimport B and reopen A: only the cell of "+moved.Id+" differs, (2,4) -> (3,4)");window.Close();
        }
    }
}
