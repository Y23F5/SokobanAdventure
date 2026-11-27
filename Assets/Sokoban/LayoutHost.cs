/**
 * @file LayoutHost.cs
 * @brief Awake 打开编辑会话并恢复镜头；FrameDocument 在换文档时重新取景。Update 按起手位置分派地图和 UI 输入，
 * DragCamera 将屏幕拖动转换成绕焦点旋转或地面平移；ViewSize 根据可用画幅计算缩放，UpdateCamera 应用草稿视角。
 * LateUpdate 同步正式与半透明模型，RenderOverlay 对齐网格和选择框；RequestQuit 和 Leave 保护未保存现场。
 * StartTrial 先让 Panel.Mode 进入试玩模式开启输入遮罩，再对目录内每个房间调用 ValidatedRoom.From 做发布级校验；失败时退回上一模式，经 Panel 上报规则、定位与修复入口，
 * 成功后在同一帧生成运行副本并最后关闭编辑界面。ValidateTrial 逐房校验并返回首个失败的规则问题与完整说明，FixEntry 把建议工具翻译成修复入口文字。
 * EndTrial 保留编辑会话，退出试玩模式并重新投影编辑相机与模型。通过 RuntimeLayoutUI 的输入遮罩决定是否允许地图输入，
 * Update 的地图输入遮罩读取 Panel.Mode.BlocksMapInput，不再依赖独立的界面状态。
 */
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban
{
    public sealed class LayoutHost : MonoBehaviour
    {
        public LayoutPanel Panel { get; private set; }
        public bool GridVisible = true;
        Vector2Int? rectangleStart;
        public string HoverFeedback { get; private set; }
        public string AppearanceIssue => preview.AppearanceIssue;
        public RuntimeLayoutUI Interface;
        readonly RoomPreview preview = new RoomPreview();
        LayoutOverlay overlay;
        Camera lens;
        DioramaLighting lighting;
        string litTheme;
        Vector3 pan, lastPointer, downPointer;
        float zoom = 1, yaw, orbitDrag;
        int gesture = -1;
        bool dragged, quitting;
        Vector2Int hoveredCell;
        bool hovering;
        PuzzleHost trial;
        bool trialStarting;

        public void StartTrial()
        {
            if (trial || trialStarting || Panel.Modal) return;
            trialStarting = true;
            try
            {
                if (!Panel.Mode.Enter(Core.UiMode.Trial))
                {
                    Panel.ReportIssue(Panel.Mode.Current == Core.UiMode.Check ? "无法开始试玩：请先关闭检查页" : "无法开始试玩：当前界面模式不允许试玩");
                    return;
                }
                var failure = ValidateTrial(out var failedRoom, out var report);
                if (failure != null)
                {
                    Panel.Mode.Back();
                    if (failedRoom == Panel.Draft.ActiveId) Panel.LocateIssue(failure);
                    Panel.ReportIssue(report);
                    return;
                }
                if (!Panel.Flush()) { Panel.Mode.Back(); return; }
                var prefab = Resources.Load<PuzzleHost>("PuzzleSession");
                var run = PuzzleRun.FromDraft(Panel.Draft);
                trial = Instantiate(prefab);
                trial.Begin(run, true, EndTrial);
                gesture = -1;
                hovering = false;
                preview.Clear();
                overlay.Dispose();
                overlay = null;
                Interface.gameObject.SetActive(false);
            }
            catch (System.Exception error)
            {
                EndTrial();
                Panel.ReportIssue("无法开始试玩：" + error.Message);
            }
            finally { trialStarting = false; }
        }

        Core.Issue ValidateTrial(out string roomId, out string report)
        {
            roomId = null;
            report = null;
            foreach (var pair in Panel.Draft.Catalog)
            {
                try { Core.ValidatedRoom.From(pair.Value, pair.Value.Meta); }
                catch (System.InvalidOperationException error)
                {
                    var issue = Core.RoomValidator.ValidateDraft(pair.Value, Core.ValidationStage.Publish).FirstOrDefault(i => i.Severity == Core.Severity.Error)
                        ?? new Core.Issue("validation.failed", Core.Severity.Error, pair.Key, error.Message);
                    var target = Core.IssueLocator.Locate(issue);
                    roomId = pair.Key;
                    report = "无法开始试玩，房间 " + pair.Key + " 未通过发布级校验：" + error.Message + "｜定位：" + issue.Location + "｜修复入口：" + FixEntry(target.Tool);
                    return issue;
                }
            }
            return null;
        }

        static string FixEntry(Core.LocateTool tool)
        {
            switch (tool)
            {
                case Core.LocateTool.PlaceSpawn: return "放置出生点";
                case Core.LocateTool.PlaceTarget: return "放置目标";
                case Core.LocateTool.PaintWall: return "用墙壁工具补全边界";
                case Core.LocateTool.SetDoor: return "设置门";
                case Core.LocateTool.AddRecoveryItem: return "添加恢复道具";
                case Core.LocateTool.Select: return "选择该对象检查";
                default: return "打开检查页按规则逐项处理";
            }
        }

        void EndTrial()
        {
            if (trial)
            {
                trial.gameObject.SetActive(false);
                Destroy(trial.gameObject);
                trial = null;
            }
            if (Panel.Mode.Current == Core.UiMode.Trial) Panel.Mode.Back();
            Interface.gameObject.SetActive(true);
            if (overlay == null) overlay = new LayoutOverlay();
            UpdateCamera();
            preview.Sync(Panel.Draft, solo);
            preview.ClipWalls(Panel.Draft, lens.transform.rotation);
        }

        void Awake()
        {
            lens = Camera.main;
            lighting = new DioramaLighting(lens);
            Panel = new LayoutPanel("runtime-editor");
            Panel.DocumentOpened += FrameDocument;
            if (Panel.HasView)
            {
                pan = Panel.Pivot;
                zoom = Panel.Zoom;
                if (Panel.Rotation != Quaternion.identity)
                {
                    yaw = CameraRig.Side(Panel.Rotation.eulerAngles.y) * 90f;
                }
            }
            overlay = new LayoutOverlay();
            Application.wantsToQuit += RequestQuit;
            UpdateCamera();
        }

        string solo;
        Vector3 savedPan;
        float savedZoom, savedYaw;
        public void ToggleRoomView()
        {
            var d=Panel.Draft;
            if(d.World==null || d.Placement==null) { Panel.Status="当前是独立房间"; return; }
            if(solo==null) {savedPan=pan;savedZoom=zoom;savedYaw=yaw;solo=d.ActiveId;pan=Vector3.zero;zoom=1;}
            else {solo=null;pan=savedPan;zoom=savedZoom;yaw=savedYaw;}
            UpdateCamera();
        }
        void FrameDocument()
        {
            solo=null;
            pan = Vector3.zero;
            zoom = 1;
            yaw = 0;
            orbitDrag = 0;
            gesture = -1;
            Panel.CancelTool();
            var draft = Panel.Draft;
            if (draft.World != null && draft.World.Rooms.Count > 0)
            {
                var lower = new Vector2Int(int.MaxValue, int.MaxValue);
                var upper = new Vector2Int(int.MinValue, int.MinValue);
                foreach (var placement in draft.World.Rooms)
                {
                    var origin = new Vector2Int(placement.Origin.X, placement.Origin.Y);
                    var room = draft.Catalog[placement.LevelId].Room;
                    lower = Vector2Int.Min(lower, origin);
                    upper = Vector2Int.Max(upper, origin + new Vector2Int(room.Width, room.Height) - Vector2Int.one);
                }
                var worldMin = new Vector2Int(draft.World.Min.X, draft.World.Min.Y);
                var worldMax = new Vector2Int(draft.World.Max.X, draft.World.Max.Y);
                var offset = ((Vector2)(lower-worldMin) + (Vector2)(upper-worldMax)) * .5f;
                pan = new Vector3(offset.x,0,offset.y);
                zoom = ViewSize(upper-lower+Vector2Int.one) / ViewSize(worldMax-worldMin+Vector2Int.one);
            }
            UpdateCamera();
        }

        Vector2Int DraftOrigin => new Vector2Int(Panel.Draft.Origin.X, Panel.Draft.Origin.Y);

        static Sokoban.Core.Cell ToCell(Vector2Int v) => new Sokoban.Core.Cell(v.x, v.y);

        float ViewSize(Vector2Int span)
        {
            float diagonal = span.magnitude;
            float halfWidth = diagonal * .5f + 1.3f;
            float halfHeight = diagonal * .5f * Mathf.Sin(CameraRig.Pitch * Mathf.Deg2Rad) + 1.1f;
            return Mathf.Max(halfHeight, halfWidth / Mathf.Max(.1f, lens.aspect * .74f));
        }

        void UpdateCamera()
        {
            var draft = Panel.Draft;
            Vector2Int min = new Vector2Int(draft.Origin.X, draft.Origin.Y), span = new Vector2Int(draft.Room.Width, draft.Room.Height);
            if (draft.World != null && solo==null)
            {
                min = new Vector2Int(draft.World.Min.X, draft.World.Min.Y);
                span = new Vector2Int(draft.World.Max.X, draft.World.Max.Y) - min + Vector2Int.one;
            }
            lens.orthographic = true;
            lens.orthographicSize = ViewSize(span) * zoom;
            var target = pan + new Vector3(min.x+(span.x-1)*.5f,0,min.y+(span.y-1)*.5f);
            CameraRig.Place(lens, target, yaw, span.magnitude + 12, .26f * lens.orthographicSize * lens.aspect);
            Panel.SetView(pan,lens.transform.rotation,zoom,true);
        }

        void DragCamera(Vector3 delta)
        {
            if (gesture == 1)
            {
                orbitDrag += delta.x;
                int turns = (int)(orbitDrag / 100f);
                yaw += turns * 90f;
                orbitDrag -= turns * 100f;
            }
            else
            {
                var forward = Vector3.Cross(lens.transform.right, Vector3.up);
                float units = 2 * lens.orthographicSize / Mathf.Max(1, Screen.height);
                pan -= (lens.transform.right * delta.x + forward * delta.y / Mathf.Sin(CameraRig.Pitch * Mathf.Deg2Rad)) * units;
            }
            UpdateCamera();
        }

        void Update()
        {
            if (trial) return;
            Vector3 pointer = Input.mousePosition;
            bool modal = Interface ? Interface.CoversMap : Panel.Modal || Panel.Mode.BlocksMapInput;
            hovering = !modal && (!Interface || !Interface.PointerOverUi(pointer)) && new Rect(0,0,Screen.width,Screen.height).Contains(pointer)
                && RoomPreview.CellFromRay(lens.ScreenPointToRay(pointer), out hoveredCell);
            if (modal) { gesture = -1; return; }
            if (Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E))
            {
                yaw += Input.GetKeyDown(KeyCode.Q) ? -90 : 90;
                UpdateCamera();
            }
            int turns = Input.GetKeyDown(KeyCode.LeftBracket) ? -1 : Input.GetKeyDown(KeyCode.RightBracket) ? 1 : 0;
            if (turns != 0 && !(UnityEngine.EventSystems.EventSystem.current && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject && UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>()))
            {
                var draft = Panel.Draft;
                if (draft.Tool == RoomTool.Select && !draft.SelectedCell.HasValue && draft.Model.RecoveryDevice != null)
                    Panel.Run(() => draft.RotateRecoveryDevice(turns), "已旋转恢复装置");
                else if (draft.Tool == RoomTool.Select) Panel.Run(() => draft.RotateSelection(turns), "已旋转所选内容");
                else if (draft.Tool != RoomTool.Move && draft.Tool != RoomTool.Erase) draft.RotateBrush(turns);
            }
            if (Input.GetKeyDown(KeyCode.Escape)) { bool idle = Panel.Draft.Tool == RoomTool.Select && !Panel.Draft.SelectedCell.HasValue && !Panel.PlacingRoom && !Panel.Painting; rectangleStart=null; Panel.CancelTool(); gesture = -1; if (idle) Leave(); }
            if (hovering && Input.mouseScrollDelta.y != 0)
            {
                zoom = Mathf.Clamp(zoom * Mathf.Pow(1.12f,-Input.mouseScrollDelta.y),.1f,5);
                UpdateCamera();
            }
            if (solo != null && solo != Panel.Draft.ActiveId) solo = null;
            if (solo != null && !Panel.IsBrush && !Panel.Draft.Room.Inside(ToCell(hoveredCell - DraftOrigin))) hovering = false;
            if (gesture == -1 && hovering)
            {
                for (int i=0;i<=2;i++)
                {
                    if (!Input.GetMouseButtonDown(i)) continue;
                    orbitDrag = 0;
                    gesture=i; downPointer=lastPointer=pointer; dragged=false;
                    if (i==0)
                    {
                        if (!Panel.PlacingRoom && Panel.IsBrush) Panel.StartStroke(hoveredCell);
                        else if (Panel.Draft.Tool == RoomTool.Select && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                            rectangleStart = hoveredCell - DraftOrigin;
                        else if (Panel.Draft.Tool == RoomTool.Select && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)))
                            Panel.Draft.Select(ToCell(hoveredCell - DraftOrigin), true);
                        else Panel.ClickWorld(hoveredCell);
                    }
                    break;
                }
            }
            if (gesture < 0) return;
            bool beganDrag = !dragged && (pointer-downPointer).sqrMagnitude >= 16;
            dragged |= beganDrag;
            if (dragged && gesture != 0) DragCamera(pointer - (beganDrag ? downPointer : lastPointer));
            if (hovering && gesture==0 && Panel.Painting) Panel.ExtendStroke(hoveredCell);
            lastPointer=pointer;
            if (!Input.GetMouseButtonUp(gesture)) return;
            if (gesture == 0 && rectangleStart.HasValue)
            {
                if (hovering) Panel.Draft.SelectRectangle(ToCell(rectangleStart.Value), ToCell(hoveredCell - DraftOrigin));
                rectangleStart = null;
            }
            if (gesture == 0 && Panel.Painting) Panel.EndStroke();
            if (gesture == 1 && !dragged && hovering)
            {
                if (Panel.Draft.Tool == RoomTool.Erase) { Panel.StartStroke(hoveredCell); Panel.EndStroke(); }
                else Panel.CancelTool();
            }
            gesture=-1;
        }

        void LateUpdate()
        {
            if (trial) return;
            var theme = Panel.Draft.Model.Meta.ThemeId ?? "warehouse";
            if (theme != litTheme) { lighting.Apply(DioramaLighting.Theme(theme),1); litTheme = theme; }
            UpdateCamera();
            preview.Sync(Panel.Draft, solo);
            preview.ClipWalls(Panel.Draft, lens.transform.rotation);
            HoverFeedback = null;
            if (hovering && Panel.PlacingRoom && Panel.Draft.World != null)
            {
                var tint = Panel.Candidate(hoveredCell,out var issue);
                preview.ShowPlacement(Panel.Draft,hoveredCell,tint);
                HoverFeedback=issue ?? "放置房间至 "+hoveredCell;
            }
            else preview.HidePlacement();
            string itemIssue = overlay.Draw(Panel.Draft,GridVisible,hovering && !Panel.PlacingRoom,hoveredCell);
            HoverFeedback = HoverFeedback ?? itemIssue;
            Panel.Tick();
        }

        public void Leave() => Panel.Exit(() => SceneManager.LoadSceneAsync("MainMenu",LoadSceneMode.Single));
        bool RequestQuit()
        {
            if (trial) return Panel.Flush();
            if (quitting || !Panel.Draft.Dirty) return Panel.Flush();
            Panel.RequestExit(()=>{ quitting=true; Application.Quit(); });
            return false;
        }
        void OnApplicationFocus(bool focus) { if (!focus) { rectangleStart=null; gesture=-1; Panel?.Flush(); } }
        void OnDestroy()
        {
            lighting?.Dispose();
            Application.wantsToQuit -= RequestQuit;
            Panel?.Flush();
            preview.Clear();
            overlay?.Dispose();
        }
    }
}
