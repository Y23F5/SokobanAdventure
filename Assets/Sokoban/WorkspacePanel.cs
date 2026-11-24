/**
 * @file WorkspacePanel.cs
 * @brief InitSlot、Tick 与 Flush 读取恢复槽，按空闲和连续编辑时限原子写盘。
 * SetView 和 RestoreView/TrackView 交换摄像机状态；NewRoom/Exit 保护工作文件。
 * WorkspaceControls 处理房间目录、原点、属性与检查。ClickWorld 选房并换算局部格。
 * StartStroke/ExtendStroke/EndStroke 合并一笔，CancelTool 清除暂存，DrawStroke 绘制待提交格。
 */
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Sokoban
{
    public sealed partial class LayoutPanel
    {
        string slotPath,fieldX="0",fieldY="0",roomName="";
        public bool RecoverySlotExists => slotPath != null && File.Exists(slotPath);
        int observedRevision=-1;
        RoomTool observedTool;
        bool changed,discardSlot;
        double firstChange,lastChange;
        Action exitAction;
        readonly HashSet<Vector2Int> stroke=new HashSet<Vector2Int>();
        Vector2Int lastCell;
        public bool Painting {get;private set;}
        public bool PlacingRoom { get; private set; }
        string placementId;
        int issueRevision = -1;
        string structuralIssue;
        public string StructuralIssue {
            get {
                if (issueRevision != Draft.Revision) {
                    issueRevision = Draft.Revision;
                    structuralIssue = (Draft.World == null ? RoomFile.StructureIssues(Draft.Room) : WorldLayout.Issues(Draft.World, Draft.Catalog)).FirstOrDefault();
                }
                return structuralIssue;
            }
        }
        public void StartRoomPlacement() {
            if (Draft.World == null) return;
            CancelTool();
            placementId = Draft.ActiveId;
            PlacingRoom = true;
            Status = "移动鼠标预览整房，左键放置，短按右键或 Esc 取消";
        }
        public Color Candidate(Vector2Int origin, out string reason) {
            reason = WorldLayout.PlacementIssue(Draft.World, Draft.Catalog, Draft.ActiveId, origin, Draft.Placement != null);
            if (reason != null) return Color.red;
            reason = WorldLayout.ConnectionIssues(Draft.World, Draft.Catalog, Draft.ActiveId, origin).FirstOrDefault();
            return reason == null ? Color.green : Color.yellow;
        }
        public void ConfirmRoom(Vector2Int origin) {
            if (Draft.World == null || placementId != Draft.ActiveId) { CancelTool(); return; }
            Color state = Candidate(origin, out string reason);
            if (state == Color.red) { Status = reason; return; }
            Draft.PlaceRoom(origin, Draft.Placement != null);
            CancelTool();
            Status = reason ?? "房间已放置到 " + origin;
        }
        public bool IsBrush => Draft.Tool==RoomTool.Floor || Draft.Tool==RoomTool.Wall || Draft.Tool==RoomTool.Erase;
        public bool HasView {get;private set;}
        public Vector3 Pivot {get;private set;}
        public Quaternion Rotation {get;private set;}=Quaternion.identity;
        public float Zoom {get;private set;}=1;
        public bool Ortho {get;private set;}=true;
        void InitSlot(string slot,string recoveryDirectory) {
            if(slot==null || Application.isBatchMode && recoveryDirectory==null) return;
            slotPath=Path.Combine(recoveryDirectory??Application.persistentDataPath,slot+"-ws02.json");
            if(!File.Exists(slotPath))return;
            try {
                var data=JObject.Parse(File.ReadAllText(slotPath));var view=data["view"];
                if(view is JObject) {var pos=(JArray)view["p"];var rot=(JArray)view["r"];SetView(new Vector3((float)pos[0],(float)pos[1],(float)pos[2]),new Quaternion((float)rot[0],(float)rot[1],(float)rot[2],(float)rot[3]),(float)view["zoom"],(bool)view["ortho"]);}
                Draft.Recover(data);Status="已恢复上次编辑现场";
            } catch(Exception ex) {Draft.Recover(new RoomDraft(Resources.Load<TextAsset>("DefaultRoom").text).Snapshot());HasView=false;Status="草稿恢复失败，已打开默认房间："+ex.Message;}
        }
        void Changed() {double now=Time.realtimeSinceStartupAsDouble;if(!changed)firstChange=now;changed=true;lastChange=now;}
        public void Tick() {
            if(slotPath==null || discardSlot)return;
            if(observedRevision!=Draft.Revision || observedTool!=Draft.Tool) {Changed();observedRevision=Draft.Revision;observedTool=Draft.Tool;}
            double now=Time.realtimeSinceStartupAsDouble;
            if(changed && (now-lastChange>=1 || now-firstChange>=10)) Flush();
        }
        public bool Flush() {
            if(slotPath==null || discardSlot)return true;
            try {Directory.CreateDirectory(Path.GetDirectoryName(slotPath));var data=Draft.Snapshot();
                if(HasView) data["view"]=new JObject { ["p"]=new JArray(Pivot.x,Pivot.y,Pivot.z),["r"]=new JArray(Rotation.x,Rotation.y,Rotation.z,Rotation.w),["zoom"]=Zoom,["ortho"]=Ortho };
                RoomFile.Write(slotPath,data.ToString());changed=false; return true;
            } catch(Exception ex) {Status="草稿保存失败："+ex.Message;firstChange=lastChange=Time.realtimeSinceStartupAsDouble;return false;}
        }
        public void SetView(Vector3 pivot,Quaternion rotation,float zoom,bool ortho) {
            if(HasView && Pivot==pivot && Rotation==rotation && Mathf.Approximately(Zoom,zoom) && Ortho==ortho)return;
            HasView=true;Pivot=pivot;Rotation=rotation;Zoom=zoom;Ortho=ortho;Changed();
        }
#if UNITY_EDITOR
        public void RestoreView(UnityEditor.SceneView view) {view.LookAtDirect(Pivot,Rotation,Zoom);view.orthographic=Ortho;}
        public void TrackView(UnityEditor.SceneView view) => SetView(view.pivot,view.rotation,view.size,view.orthographic);
        public void DrawStroke() {foreach(var cell in stroke) UnityEditor.Handles.DrawWireCube(new Vector3(cell.x,.05f,cell.y),new Vector3(1,.03f,1));}
#endif
        public void NewRoom() => Guard(()=>{Draft.NewRoom();DocumentOpened?.Invoke();Flush();Status="已创建独立房间";});
        public void RequestExit(Action exit)
        {
            operation = null;
            pending = afterSave = null;
            Exit(exit);
        }
        public void Exit(Action exit) {
            if(!Draft.Dirty) {if(Flush()) exit();return;}
            exitAction=exit;
            Guard(()=>{
                if(Draft.Dirty) {
                    if(slotPath!=null && File.Exists(slotPath))File.Delete(slotPath);
                    discardSlot=true;
                } else if(!Flush()) return;
                exitAction=null;exit();
            });
        }
        public void ClickWorld(Vector2Int world) {
            if (PlacingRoom) { ConfirmRoom(world); return; }
            if(Draft.World!=null) {
                var id=Draft.RoomAt(world);
                if(Draft.Tool==RoomTool.Select && id!=null && id!=Draft.ActiveId) Draft.UseRoom(id);
                if(Draft.Placement==null) {Status="房间尚未放入世界";return;}
            }
            Click(world-Draft.Origin);
        }
        public string ConnectionAction(JToken link) => (string)link["id"] + ((string)link["mode"] == "direct" ? " · 直连 → 改为传送" : " · 传送 → 改为直连");
        public void ToggleConnection(JToken link) => Run(() => Draft.SetConnectionMode((string)link["id"], (string)link["mode"] == "direct" ? "teleport" : "direct"), "已更新门连接");
        public void WorkspaceControls() {
            GUILayout.Label(Draft.World==null?"独立房间":"世界 · "+(string)Draft.World["worldId"]);
            if(Draft.World==null) {if(GUILayout.Button("以此房间创建世界")) Run(()=>{Draft.BeginWorld();Flush();},"房间已加入目录，请设置放置原点");}
            else {
                foreach(var prop in Draft.Catalog.Properties()) if(GUILayout.Button((Draft.ActiveId==prop.Name?"● ":"")+(string)prop.Value["name"]+"  "+prop.Name)) Draft.UseRoom(prop.Name);
                foreach (var link in Draft.World["connections"])
                    if (GUILayout.Button(ConnectionAction(link))) ToggleConnection(link);
                if(GUILayout.Button("从文件加入房间"))ChooseFile("加入房间");
                GUILayout.Label(Draft.Placement==null?"尚未放置":"房间原点 "+Draft.Origin);
                if(GUILayout.Button(Draft.Placement==null?"在地图放置整房":"在地图移动整房")) StartRoomPlacement();
            }
            GUILayout.BeginHorizontal(); GUILayout.Label("X",GUILayout.Width(34));fieldX=GUILayout.TextField(fieldX);GUILayout.Label("Y",GUILayout.Width(34));fieldY=GUILayout.TextField(fieldY);GUILayout.EndHorizontal();
            bool valid=int.TryParse(fieldX,out int x) & int.TryParse(fieldY,out int y);
            var point=new Vector2Int(x,y);
            if(Draft.World!=null) {
                if(GUILayout.Button(Draft.Placement==null?"放置房间至坐标":"平移房间至坐标")) Run(()=>{RoomFile.Check(valid,"需要整数坐标");Draft.PlaceRoom(point,Draft.Placement!=null);},"已更新房间原点");
                if(Draft.Placement!=null && GUILayout.Button("从世界移出房间")) Run(Draft.RemoveRoom,"房间已保留在目录中");
                if(GUILayout.Button("检查世界")) {var issues=WorldLayout.Issues(Draft.World,Draft.Catalog).ToArray();Status=issues.Length==0?"放置与直连检查通过":string.Join("；",issues);}
            }
            if(Draft.Selected!=null && GUILayout.Button("将选中对象移至坐标")) Run(()=>{RoomFile.Check(valid,"需要整数坐标");Draft.Move(point-Draft.Origin);},"已修改格子坐标");
            roomName=GUILayout.TextField(roomName); if(GUILayout.Button("应用房间名称")) Run(()=>Draft.Rename(roomName),"已更新名称");
        }
        public void CancelTool() {PlacingRoom=false;placementId=null;Painting=false;stroke.Clear();Draft.Tool=RoomTool.Select;Status="已返回选择";}
        public void StartStroke(Vector2Int point) {Painting=true;stroke.Clear();lastCell=point;stroke.Add(point);}
        public void ExtendStroke(Vector2Int point) {
            var delta=point-lastCell;int steps=Math.Max(Math.Abs(delta.x),Math.Abs(delta.y));
            for(int step=1;step<=steps;step++) stroke.Add(Vector2Int.RoundToInt(Vector2.Lerp(lastCell,point,(float)step/steps)));
            lastCell=point;
        }
        public void EndStroke() {
            if(!Painting)return;Painting=false;
            Status=Draft.World!=null && Draft.Placement==null?"房间尚未放置":Draft.Brush(stroke.Select(p=>p-Draft.Origin),Draft.Tool)??"笔划已提交";stroke.Clear();
        }
    }
}
