/**
 * @file WorkspacePanel.cs
 * @brief InitSlot 读取恢复槽，失败时先保留原始文件才启用默认房间的自动保存。LoadView 恢复视角，视角损坏时保留已读草稿。Tick 与 Flush 按空闲和连续编辑时限原子写盘。
 * SetView 和 RestoreView/TrackView 交换摄像机状态；NewRoom/Exit 保护工作文件。
 * WorkspaceControls 处理房间目录、原点、属性与检查。ClickWorld 选房并换算局部格。
 * Issues 按草稿修订号缓存 Core 的发布级校验结果与世界布局问题，独立于状态栏的操作反馈；StructuralIssue 取第一条错误，RuleOk 与 RuleHint 按规则 ID 查询必填项清单。
 * LocateIssue 经 Core 的 IssueLocator 跳转：格类问题选中该格，房间级问题选中第一个空地板格，并按建议工具切到放置目标或画墙，同时从检查模式回到上一模式。
 * CheckCurrentRoom 打开结构问题列表，CloseCheck 回到上一模式。
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
        string slotProtectionError;
        double firstChange,lastChange;
        Action exitAction;
        readonly HashSet<Vector2Int> stroke=new HashSet<Vector2Int>();
        Vector2Int lastCell;
        public bool Painting {get;private set;}
        public bool PlacingRoom { get; private set; }
        string placementId;
        int issueRevision = -1;
        System.Collections.Generic.List<Sokoban.Core.Issue> issues = new System.Collections.Generic.List<Sokoban.Core.Issue>();
        public System.Collections.Generic.IReadOnlyList<Sokoban.Core.Issue> Issues {
            get {
                if (issueRevision != Draft.Revision) {
                    issueRevision = Draft.Revision;
                    issues = Sokoban.Core.RoomValidator.ValidateDraft(Draft.Model, Sokoban.Core.ValidationStage.Publish);
                    if (Draft.World != null)
                        foreach (var text in WorldLayout.Issues(Draft.World, Draft.Catalog))
                            issues.Add(new Sokoban.Core.Issue("world.layout", Sokoban.Core.Severity.Error, Draft.ActiveId, text));
                }
                return issues;
            }
        }
        public string StructuralIssue
        {
            get
            {
                var first = Issues.FirstOrDefault(i => i.Severity == Sokoban.Core.Severity.Error);
                if (first == null) return null;
                return Draft.World != null && first.RuleId != "world.layout" ? Draft.ActiveId + "：" + first.Message : first.Message;
            }
        }
        public bool RuleOk(string ruleId) => !Issues.Any(i => i.RuleId == ruleId);
        public string RuleHint(string ruleId) => Issues.FirstOrDefault(i => i.RuleId == ruleId)?.Message;
        public void LocateIssue(Sokoban.Core.Issue issue) {
            var target = Sokoban.Core.IssueLocator.Locate(issue);
            if (Mode.Current == Sokoban.Core.UiMode.Check) Mode.Back();
            CancelTool();
            if (target.Kind == Sokoban.Core.LocateKind.Cell) Draft.Select(target.Cell);
            else if (target.Kind == Sokoban.Core.LocateKind.Room) SelectFirstEmptyFloor();
            if (target.Tool == Sokoban.Core.LocateTool.PlaceTarget) Draft.Tool = RoomTool.Target;
            else if (target.Tool == Sokoban.Core.LocateTool.PaintWall) Draft.Tool = RoomTool.Wall;
            Status = issue.Message;
        }
        void SelectFirstEmptyFloor() {
            var room = Draft.Room;
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++) {
                    var cell = new Sokoban.Core.Cell(x, y);
                    if (room.TerrainAt(cell).CategoryId != Sokoban.Core.TerrainCategories.Floor || room.ObjectAt(cell) != null) continue;
                    Draft.Select(cell);
                    return;
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
            reason = WorldLayout.PlacementIssue(Draft.World, Draft.Catalog, Draft.ActiveId, new Sokoban.Core.Cell(origin.x, origin.y), Draft.Placement != null);
            if (reason != null) return Color.red;
            reason = WorldLayout.ConnectionIssues(Draft.World, Draft.Catalog, Draft.ActiveId, new Sokoban.Core.Cell(origin.x, origin.y)).FirstOrDefault();
            return reason == null ? Color.green : Color.yellow;
        }
        public void ConfirmRoom(Vector2Int origin) {
            if (Draft.World == null || placementId != Draft.ActiveId) { CancelTool(); return; }
            Color state = Candidate(origin, out string reason);
            if (state == Color.red) { Status = reason; return; }
            Draft.PlaceRoom(new Sokoban.Core.Cell(origin.x, origin.y), Draft.Placement != null);
            CancelTool();
            Status = reason ?? "房间已放置到 " + origin;
        }
        public bool IsBrush => Draft.Tool==RoomTool.Floor || Draft.Tool==RoomTool.Wall || Draft.Tool==RoomTool.Erase;
        public bool HasView {get;private set;}
        public Vector3 Pivot {get;private set;}
        public Quaternion Rotation {get;private set;}=Quaternion.identity;
        public float Zoom {get;private set;}=1;
        public bool Ortho {get;private set;}=true;
        void InitSlot(string slot, string recoveryDirectory)
        {
            if (slot == null || Application.isBatchMode && recoveryDirectory == null) return;
            slotPath = Path.Combine(recoveryDirectory ?? Application.persistentDataPath, slot + "-ws02.json");
            if (!File.Exists(slotPath)) return;
            try
            {
                Draft.Recover(File.ReadAllText(slotPath));
            }
            catch (Exception error)
            {
                Draft.NewDefaultRoom();
                string backup = slotPath + ".unreadable-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.Copy(slotPath, backup);
                    Status = "草稿恢复失败，原文件已保留为 " + Path.GetFileName(backup) + "：" + error.Message;
                }
                catch (Exception backupError)
                {
                    slotProtectionError = "无法备份原草稿，已关闭自动保存：" + backupError.Message;
                    Status = slotProtectionError;
                }
                return;
            }
            Status = "已恢复上次编辑现场";
            try { LoadView(); }
            catch (Exception error) { Status = "草稿已恢复，视角读取失败：" + error.Message; }
        }

        void LoadView()
        {
            var viewPath = slotPath + ".view";
            if (!File.Exists(viewPath)) return;
            var fields = File.ReadAllText(viewPath).Split(',')
                .Select(value => float.Parse(value, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (fields.Length == 9)
                SetView(new Vector3(fields[0], fields[1], fields[2]),
                    new Quaternion(fields[3], fields[4], fields[5], fields[6]), fields[7], fields[8] > 0);
        }

        void Changed() {double now=Time.realtimeSinceStartupAsDouble;if(!changed)firstChange=now;changed=true;lastChange=now;}
        public void Tick() {
            if(slotPath==null || discardSlot || slotProtectionError!=null)return;
            if(observedRevision!=Draft.Revision || observedTool!=Draft.Tool) {Changed();observedRevision=Draft.Revision;observedTool=Draft.Tool;}
            double now=Time.realtimeSinceStartupAsDouble;
            if(changed && (now-lastChange>=1 || now-firstChange>=10)) Flush();
        }
        public bool Flush() {
            if(slotPath==null || discardSlot)return true;
            if (slotProtectionError != null) { Status = slotProtectionError; return false; }
            try {Directory.CreateDirectory(Path.GetDirectoryName(slotPath));string data=Draft.Snapshot();
                if(HasView) File.WriteAllText(slotPath+".view",string.Join(",",new[]{Pivot.x,Pivot.y,Pivot.z,Rotation.x,Rotation.y,Rotation.z,Rotation.w,Zoom,Ortho?1f:0f}.Select(v=>v.ToString("R",System.Globalization.CultureInfo.InvariantCulture))));
                Sokoban.Core.AtomicFile.Write(slotPath, data, null);changed=false; return true;
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
        public void OpenBlueprint(RoomDraft prepared) => Guard(() => {
            Draft.Recover(prepared.Snapshot());
            DocumentOpened?.Invoke();
            Flush();
        });
        public void NewRoom(bool blank = false) => Guard(()=>{if (blank) Draft.NewBlankRoom(); else Draft.NewDefaultRoom();DocumentOpened?.Invoke();Flush();Status="已创建独立房间";});
        public void NewDefaultRoom() => Guard(()=>{Draft.NewDefaultRoom();DocumentOpened?.Invoke();Flush();Status="已从默认布局新建";});
        public void RequestExit(Action exit)
        {
            operation = null;
            pending = afterSave = null;
            Exit(exit);
        }
        public void Exit(Action exit) {
            if(!Draft.Dirty) {if(slotProtectionError!=null || Flush()) exit();return;}
            exitAction=exit;
            Guard(()=>{
                if(Draft.Dirty) {
                    if(slotProtectionError==null && slotPath!=null && File.Exists(slotPath))File.Delete(slotPath);
                    discardSlot=true;
                } else if(slotProtectionError==null && !Flush()) return;
                exitAction=null;exit();
            });
        }
        public void ClickWorld(Vector2Int world) {
            if (PlacingRoom) { ConfirmRoom(world); return; }
            if(Draft.World!=null) {
                var id=Draft.RoomAt(new Sokoban.Core.Cell(world.x,world.y));
                if(Draft.Tool==RoomTool.Select && id!=null && id!=Draft.ActiveId) Draft.UseRoom(id);
                if(Draft.Placement==null) {Status="房间尚未放入世界";return;}
            }
            Click(new Sokoban.Core.Cell(world.x,world.y)-Draft.Origin);
        }
        public string ConnectionAction(Sokoban.Core.WorldConnection link) => link.Id + (link.Mode == Sokoban.Core.WorldCodec.Direct ? " · 直连 → 改为传送" : " · 传送 → 改为直连");
        public void ToggleConnection(Sokoban.Core.WorldConnection link) => Run(() => Draft.SetConnectionMode(link.Id, link.Mode == Sokoban.Core.WorldCodec.Direct ? Sokoban.Core.WorldCodec.Teleport : Sokoban.Core.WorldCodec.Direct), "已更新门连接");
        public static readonly string[] MarkerLabels = { "设为出生点", "添加入口", "添加出口" };
        public void ApplyMarker(int index)
        {
            if (!Draft.SelectedCell.HasValue) { Status = "请在地图上选择一个地板格"; return; }
            string role = index == 0 ? "spawn" : index == 1 ? "entry" : "exit";
            Run(() => Draft.SetRoomMarker(Draft.SelectedCell.Value, role), MarkerLabels[index] + "已完成");
        }
        public void CloseCheck() => Mode.Back();
        public void CheckCurrentRoom() => Mode.Enter(Sokoban.Core.UiMode.Check);
        string objectName;
        bool nameMixed;
        int objectNameRevision = -1;
        public void WorkspaceControls() {
            GUILayout.Label(Draft.World==null?"独立房间":"世界 · "+Draft.World.Rooms.Count+" 个房间");
            if(Draft.World==null) {if(GUILayout.Button("以此房间创建世界")) Run(()=>{Draft.BeginWorld();Flush();},"房间已加入目录，请设置放置原点");}
            else {
                foreach(var prop in Draft.Catalog) if(GUILayout.Button((Draft.ActiveId==prop.Key?"● ":"")+prop.Value.Meta.Name+"  "+prop.Key)) Draft.UseRoom(prop.Key);
                foreach (var link in Draft.World.Connections)
                    if (GUILayout.Button(ConnectionAction(link))) ToggleConnection(link);
                if(GUILayout.Button("从文件加入房间"))ChooseFile("加入房间");
                GUILayout.Label(Draft.Placement==null?"尚未放置":"房间原点 "+Draft.Origin);
                if(GUILayout.Button(Draft.Placement==null?"在地图放置整房":"在地图移动整房")) StartRoomPlacement();
            }
            if (GUILayout.Button("检查结构")) CheckCurrentRoom();
            if (Draft.SelectedCell.HasValue)
                for (int i = 0; i < MarkerLabels.Length; i++) if (GUILayout.Button(MarkerLabels[i])) ApplyMarker(i);
            GUILayout.BeginHorizontal(); GUILayout.Label("X",GUILayout.Width(34));fieldX=GUILayout.TextField(fieldX);GUILayout.Label("Y",GUILayout.Width(34));fieldY=GUILayout.TextField(fieldY);GUILayout.EndHorizontal();
            bool valid=int.TryParse(fieldX,out int x) & int.TryParse(fieldY,out int y);
            var point=new Sokoban.Core.Cell(x,y);
            if(Draft.World!=null) {
                if(GUILayout.Button(Draft.Placement==null?"放置房间至坐标":"平移房间至坐标")) Run(()=>{RoomFile.Check(valid,"需要整数坐标");Draft.PlaceRoom(point,Draft.Placement!=null);},"已更新房间原点");
                if(Draft.Placement!=null && GUILayout.Button("从世界移出房间")) Run(Draft.RemoveRoom,"房间已保留在目录中");
                if(GUILayout.Button("检查世界")) {var issues=WorldLayout.Issues(Draft.World,Draft.Catalog).ToArray();Status=issues.Length==0?"放置与直连检查通过":string.Join("；",issues);}
            }
            if(Draft.Selected!=null && GUILayout.Button("将选中对象移至坐标")) Run(()=>{RoomFile.Check(valid,"需要整数坐标");Draft.Move(point-Draft.Origin);},"已修改格子坐标");
            if (Draft.BoxSelected)
                for (int i = 0; i < RoomDraft.BuiltinBoxes.Length; i++)
                {
                    string model = RoomDraft.BuiltinBoxes[i];
                    if (GUILayout.Button("绑定内置模型 · " + RoomDraft.BuiltinBoxNames[i])) Run(() => Draft.BindBuiltinBox(model),"已绑定箱体模型");
                }
            if (GUILayout.Button(RecoveryLabel)) CycleRecovery();
            if (GUILayout.Button(ThemeLabel)) CycleTheme();
            roomName=GUILayout.TextField(roomName); if(GUILayout.Button("应用房间名称")) Run(()=>Draft.Rename(roomName),"已更新名称");
        }
        public string RecoveryLabel => "恢复道具 · " + (Draft.Model.RecoveryItems.Count == 0 ? "无" : string.Join("、", Draft.Model.RecoveryItems.Select(i => i.ScriptRef)));
        public void CycleRecovery()
        {
            if (Draft.Model.RecoveryItems.Count > 0) { Status = "本房已有恢复道具，脚本与时长请在属性中修改"; return; }
            Run(() => Draft.Commit(new Sokoban.Core.AddRecoveryItemCommand(Draft.Model, new Sokoban.Core.RecoveryItem("recovery-" + Guid.NewGuid().ToString("N"), Sokoban.Core.Registries.BuiltinRecoveryScripts[0], 3f))), "已添加恢复道具");
        }
        public string ThemeLabel => "房间主题 · " + AppearanceLibrary.ThemeName(Draft.Theme);
        public void CycleTheme()
        {
            var themes = AppearanceLibrary.Themes;
            string next = themes[(Array.IndexOf(themes, Draft.Theme) + 1) % themes.Length];
            Run(() => Draft.SetTheme(next), "已切换房间主题，已放置素材保留原样");
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
            Status=Draft.World!=null && Draft.Placement==null?"房间尚未放置":Draft.Brush(stroke.Select(p=>new Sokoban.Core.Cell(p.x,p.y)-Draft.Origin),Draft.Tool)??"笔划已提交";stroke.Clear();
        }
    }
}
