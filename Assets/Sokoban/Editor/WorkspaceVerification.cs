/**
 * @file WorkspaceVerification.cs
 * @brief Run 通过正式样例验证世界坐标、编辑历史、交换和旧版回归并打包；Check 将断言结果写到独立证据目录。
 */
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using Newtonsoft.Json.Linq;

namespace Sokoban.Editor
{
    public static class WorkspaceVerification
    {
        static string evidence;
        static void Check(bool condition,string message) {
            File.AppendAllText(Path.Combine(evidence,"checks.txt"),(condition?"PASS ":"FAIL ")+message+Environment.NewLine);
            if(!condition) throw new InvalidOperationException(message);
        }
        static void VerifyRecovery(string source) {
            var panel=new LayoutPanel("test",evidence); panel.Draft.Tool=RoomTool.Box;panel.Draft.Place(true,new Vector2Int(2,4));
            var selected=panel.Draft.Selected;panel.SetView(new Vector3(4,0,1),Quaternion.Euler(42,15,0),8,true);panel.Tick();
            System.Threading.Thread.Sleep(1100);panel.Tick();string path=Path.Combine(evidence,"test-ws02.json");
            var restored=new LayoutPanel("test",evidence);
            Check(restored.Draft.Selected==selected && restored.Draft.Dirty && restored.Draft.Tool==RoomTool.Box && restored.Zoom==8 && restored.Pivot==new Vector3(4,0,1),"空闲一秒实盘恢复房间、选择、视角、工具、脏标记");
            DateTime first=File.GetLastWriteTimeUtc(path);var clock=System.Diagnostics.Stopwatch.StartNew();int step=0;
            while(clock.Elapsed.TotalSeconds<10.6) {panel.SetView(new Vector3(++step,0,1),Quaternion.identity,7,true);panel.Tick();System.Threading.Thread.Sleep(140);}
            Check(File.GetLastWriteTimeUtc(path)>first,"持续编辑超过十秒，期间更新磁盘快照");
            restored.Draft.Undo();restored.Draft.Redo();Check(restored.Draft.Selected==selected,"重开后历史可撤销和重做");
            string good=File.ReadAllText(path);var damaged=Path.Combine(evidence,"damaged-ws02.json");File.WriteAllText(damaged,"{");
            var fallback=new LayoutPanel("damaged",evidence);Check(fallback.Status.Contains("恢复失败") && JToken.DeepEquals(fallback.Draft.Room,JObject.Parse(source)) && File.ReadAllText(path)==good,"损坏快照回到固定房间并报告原因，其他文件不变");
        }
        static void VerifyFiles(string source) {
            var draft=new RoomDraft(source);string first=Path.Combine(evidence,"A.room.json"),second=Path.Combine(evidence,"B.room.json"),third=Path.Combine(evidence,"C.room.json");
            draft.Save(first,false);draft.Open(first,false);draft.Place(true,new Vector2Int(2,4));draft.Save(second,true);
            Check(draft.Dirty && draft.WorkPath==Path.GetFullPath(first) && !JToken.DeepEquals(JObject.Parse(File.ReadAllText(first)),draft.Room),"导出 B 不改 A 和脏标记");
            var before=draft.Snapshot();try {draft.Save(evidence,false);} catch(IOException) {} catch(UnauthorizedAccessException) {}
            try {draft.Open(Path.Combine(evidence,"damaged-ws02.json"),false);} catch(Newtonsoft.Json.JsonException) {}
            Check(JToken.DeepEquals(before,draft.Snapshot()),"不可写目标及损坏候选均保留原草稿、选择、历史与工作关联");
            draft.Save(first,false);Check(!draft.Dirty && File.ReadAllText(first)==File.ReadAllText(second),"保存回 A 并清除脏标记");
            draft.Open(second,true);Check(draft.WorkPath==null,"导入解除关联");draft.Save(third,false);Check(draft.WorkPath==Path.GetFullPath(third) && File.ReadAllText(third)==File.ReadAllText(second),"首次保存建立 C，交换内容只有房间设计");
        }
        public static void Run() {
            evidence="Docs/验收证据/WS-02/"+DateTime.Now.ToString("yyyyMMdd-HHmmss");Directory.CreateDirectory(evidence);
            string source=Resources.Load<TextAsset>("DefaultRoom").text;
            var draft=new RoomDraft(source);draft.Open("Docs/关卡样例/main.world.json",false);draft.UseRoom("factory");
            var original=draft.World.DeepClone();var room=draft.Room.DeepClone();
            Check(!WorldLayout.Issues(draft.World,draft.Catalog).Any(),"正式样例初始两条直连有效");
            draft.PlaceRoom(new Vector2Int(40,10),true);
            Check(WorldLayout.Issues(draft.World,draft.Catalog).Count()==2 && JToken.DeepEquals(original["connections"],draft.World["connections"]) && JToken.DeepEquals(room,draft.Room),"MAP02-003 工厂平移导致两条直连失效，端点与局部布局不变");
            draft.Undo();Check(JToken.DeepEquals(draft.World,original),"撤销恢复原点和直连");
            var invalid=(JObject)original.DeepClone();
            invalid["rooms"][1]["origin"]=invalid["rooms"][0]["origin"].DeepClone();
            string invalidFile=Path.Combine(evidence,"invalid-overlap.world.json");
            var published=WorldLayout.Write(Path.Combine(evidence,"valid.world.json"),(JObject)original,draft.Catalog);
            published["rooms"][1]["origin"]=published["rooms"][0]["origin"].DeepClone();
            RoomFile.Write(invalidFile,published.ToString());
            var prior=draft.Snapshot();
            bool refused=false;
            try { draft.Open(invalidFile,false); } catch(Exception ex) { refused=ex.Message.Contains("重叠"); }
            Check(refused && JToken.DeepEquals(prior,draft.Snapshot()),"MAP02-002 从磁盘打开重叠世界被拒绝并保留当前工作区");
            var size=RoomFile.Size(draft.Room);var edge=new Vector2Int(64-size.x,32-size.y);
            Check(WorldLayout.PlacementIssue(draft.World,draft.Catalog,"factory",edge,true)==null && WorldLayout.PlacementIssue(draft.World,draft.Catalog,"factory",edge+Vector2Int.right,true)!=null && WorldLayout.PlacementIssue(draft.World,draft.Catalog,"factory",new Vector2Int(8,0),true)!=null && WorldLayout.PlacementIssue(draft.World,draft.Catalog,"factory",edge,false)!=null,"MAP02-002 贴边合法、越界一格、重叠一格、重复房间拒绝");
            draft.RemoveRoom();draft.PlaceRoom(edge,false);var box=draft.Room["boxes"].First();draft.Select(RoomFile.At(box));string id=draft.Selected;
            var free=Enumerable.Range(0,size.x*size.y).Select(i=>new Vector2Int(i%size.x,i/size.x)).First(cell=>draft.PlacementError(true,cell)==null);
            draft.Move(free);draft.Tool=RoomTool.Move;var restored=new RoomDraft(source);restored.Recover(JObject.Parse(draft.Snapshot().ToString()));
            Check(restored.Selected==id && restored.Origin==edge && restored.Tool==RoomTool.Move && restored.Dirty && restored.WorkPath==draft.WorkPath,"跨会话序列化恢复选中房间、箱子、工具、原点、工作路径和未保存状态");
            restored.Undo();restored.Redo();Check(RoomFile.At(restored.Find(id))==free,"恢复后可撤销和重做局部移动");
            foreach(var link in restored.World["connections"]) link["mode"]="teleport";
            string worldFile=Path.Combine(evidence,"exchange.world.json");restored.Save(worldFile,true);Check(restored.Dirty && restored.WorkPath==draft.WorkPath,"远端房间显式改传送后导出，保留原关联与脏标记");
            var reader=new RoomDraft(source);reader.Open(worldFile,true);reader.UseRoom("factory");Check(reader.Origin==edge && RoomFile.At(reader.Find(id))==free && reader.WorkPath==null,"另端解码相对引用与局部坐标");
            reader.NewRoom();reader.Brush(new[]{new Vector2Int(3,3),new Vector2Int(4,3)},RoomTool.Wall);reader.Undo();Check(RoomFile.Floor(reader.Room,new Vector2Int(3,3)) && RoomFile.Floor(reader.Room,new Vector2Int(4,3)),"一次撤销恢复整笔");
            VerifyRecovery(source);VerifyFiles(source);
            LayoutVerification.Verify();LayoutVerification.VerifyReturn();Check(true,"WS-01 共享规则、工具窗口、文件迁移、已验收 A/B 重读回归");
            var result=BuildPipeline.BuildPlayer(new[]{"Assets/Scenes/MainMenu.unity","Assets/Scenes/Game.unity","Assets/Scenes/RuntimeEditor.unity"},"Builds/WS-02/Sokoban.exe",BuildTarget.StandaloneWindows64,BuildOptions.None);
            Check(result.summary.result==BuildResult.Succeeded && result.summary.totalErrors==0,"Windows x64 构建");
        }
    }
}
