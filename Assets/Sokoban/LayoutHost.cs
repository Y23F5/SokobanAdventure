/**
 * @file LayoutHost.cs
 * @brief Awake 打开编辑会话并恢复镜头；FrameDocument 在换文档时重新取景。Update 按起手位置分派地图和 UI 输入，
 * DragCamera 将屏幕拖动转换成绕焦点旋转或地面平移；ViewSize 根据可用画幅计算缩放，UpdateCamera 应用草稿视角。
 * LateUpdate 同步正式与半透明模型，RenderOverlay 对齐网格和选择框；RequestQuit 和 Leave 保护未保存现场。
 * StartTrial 和 EndTrial 保留编辑会话，在独立根运行草稿副本，退出后重新投影编辑相机与模型。
 */
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

namespace Sokoban
{
    public sealed class LayoutHost : MonoBehaviour
    {
        public LayoutPanel Panel { get; private set; }
        public bool GridVisible = true;
        public string HoverFeedback { get; private set; }
        public RuntimeLayoutUI Interface;
        readonly RoomPreview preview = new RoomPreview();
        readonly List<RaycastResult> hits = new List<RaycastResult>();
        LayoutOverlay overlay;
        Camera lens;
        Vector3 pan, lastPointer, downPointer;
        float zoom = 1, yaw, pitch = 66;
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
                var run = PuzzleRun.FromDraft(Panel.Draft);
                if (!Panel.Flush()) return;
                var prefab = Resources.Load<PuzzleHost>("PuzzleSession");
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
                Panel.Status = "无法开始试玩：" + error.Message;
            }
            finally { trialStarting = false; }
        }

        void EndTrial()
        {
            if (trial)
            {
                trial.gameObject.SetActive(false);
                Destroy(trial.gameObject);
                trial = null;
            }
            Interface.gameObject.SetActive(true);
            if (overlay == null) overlay = new LayoutOverlay();
            UpdateCamera();
            preview.Sync(Panel.Draft,solo);
        }

        void Awake()
        {
            lens = Camera.main;
            Panel = new LayoutPanel("runtime-editor");
            Panel.DocumentOpened += FrameDocument;
            if (Panel.HasView)
            {
                pan = Panel.Pivot;
                zoom = Panel.Zoom;
                if (Panel.Rotation != Quaternion.identity)
                {
                    yaw = Panel.Rotation.eulerAngles.y;
                    pitch = Mathf.Clamp(Panel.Rotation.eulerAngles.x, 20, 89);
                }
            }
            overlay = new LayoutOverlay();
            Application.wantsToQuit += RequestQuit;
            UpdateCamera();
        }

        string solo;
        Vector3 savedPan;
        float savedZoom, savedPitch, savedYaw;
        public void ToggleRoomView()
        {
            var d=Panel.Draft;
            if(d.World==null || d.Placement==null) { Panel.Status="当前是独立房间"; return; }
            if(solo==null) {savedPan=pan;savedZoom=zoom;savedPitch=pitch;savedYaw=yaw;solo=d.ActiveId;pan=Vector3.zero;zoom=1;}
            else {solo=null;pan=savedPan;zoom=savedZoom;pitch=savedPitch;yaw=savedYaw;}
            UpdateCamera();
        }
        void FrameDocument()
        {
            solo=null;
            pan = Vector3.zero;
            zoom = 1;
            yaw = 0;
            pitch = 66;
            gesture = -1;
            Panel.CancelTool();
            var draft = Panel.Draft;
            if (draft.World != null && draft.World["rooms"].HasValues)
            {
                var lower = new Vector2Int(int.MaxValue, int.MaxValue);
                var upper = new Vector2Int(int.MinValue, int.MinValue);
                foreach (var placement in draft.World["rooms"])
                {
                    var origin = RoomFile.Cell(placement["origin"]);
                    var room = (Newtonsoft.Json.Linq.JObject)draft.Catalog[(string)placement["levelId"]];
                    lower = Vector2Int.Min(lower, origin);
                    upper = Vector2Int.Max(upper, origin + RoomFile.Size(room) - Vector2Int.one);
                }
                var worldMin = RoomFile.Cell(draft.World["bounds"]["min"]);
                var worldMax = RoomFile.Cell(draft.World["bounds"]["max"]);
                var offset = ((Vector2)(lower-worldMin) + (Vector2)(upper-worldMax)) * .5f;
                pan = new Vector3(offset.x,0,offset.y);
                zoom = ViewSize(upper-lower+Vector2Int.one) / ViewSize(worldMax-worldMin+Vector2Int.one);
            }
            UpdateCamera();
        }

        float ViewSize(Vector2Int span) => Mathf.Max(span.y*.63f, span.x*.6f/Mathf.Max(.1f,lens.aspect*.74f));

        bool OverUI(Vector3 position)
        {
            if (!EventSystem.current) return false;
            hits.Clear();
            var data = new PointerEventData(EventSystem.current) { position = position };
            EventSystem.current.RaycastAll(data,hits);
            return hits.Count != 0;
        }

        void UpdateCamera()
        {
            var draft = Panel.Draft;
            Vector2Int min = draft.Origin, span = RoomFile.Size(draft.Room);
            if (draft.World != null && solo==null)
            {
                min = RoomFile.Cell(draft.World["bounds"]["min"]);
                span = RoomFile.Cell(draft.World["bounds"]["max"]) - min + Vector2Int.one;
            }
            lens.orthographic = true;
            lens.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            lens.orthographicSize = ViewSize(span) * zoom;
            var target = pan + new Vector3(min.x+(span.x-1)*.5f,0,min.y+(span.y-1)*.5f);
            lens.transform.position = target - lens.transform.forward * (span.magnitude + 12)
                + lens.transform.right * (.26f * lens.orthographicSize * lens.aspect);
            Panel.SetView(pan,lens.transform.rotation,zoom,true);
        }

        void DragCamera(Vector3 delta)
        {
            if (gesture == 1)
            {
                yaw += delta.x * .36f;
                pitch = Mathf.Clamp(pitch - delta.y * .25f, 20, 89);
            }
            else
            {
                var forward = Vector3.Cross(lens.transform.right, Vector3.up);
                float units = 2 * lens.orthographicSize / Mathf.Max(1, Screen.height);
                pan -= (lens.transform.right * delta.x + forward * delta.y / Mathf.Sin(pitch * Mathf.Deg2Rad)) * units;
            }
            UpdateCamera();
        }

        void Update()
        {
            if (trial) return;
            Vector3 pointer = Input.mousePosition;
            bool modal = Panel.Modal || Interface && Interface.CoversMap;
            hovering = !modal && !OverUI(pointer) && new Rect(0,0,Screen.width,Screen.height).Contains(pointer)
                && RoomPreview.CellFromRay(lens.ScreenPointToRay(pointer), out hoveredCell);
            if (modal) { gesture = -1; return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Panel.CancelTool(); gesture = -1; }
            if (hovering && Input.mouseScrollDelta.y != 0)
            {
                zoom = Mathf.Clamp(zoom * Mathf.Pow(1.12f,-Input.mouseScrollDelta.y),.1f,5);
                UpdateCamera();
            }
            if (solo != null && solo != Panel.Draft.ActiveId) solo = null;
            if (solo != null && !Panel.IsBrush && !RoomFile.Inside(Panel.Draft.Room, hoveredCell - Panel.Draft.Origin)) hovering = false;
            if (gesture == -1 && hovering)
            {
                for (int i=0;i<=2;i++)
                {
                    if (!Input.GetMouseButtonDown(i)) continue;
                    gesture=i; downPointer=lastPointer=pointer; dragged=false;
                    if (i==0)
                    {
                        if (!Panel.PlacingRoom && Panel.IsBrush) Panel.StartStroke(hoveredCell);
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
            UpdateCamera();
            preview.Sync(Panel.Draft,solo);
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
        void OnApplicationFocus(bool focus) { if (!focus) { gesture=-1; Panel?.Flush(); } }
        void OnDestroy()
        {
            Application.wantsToQuit -= RequestQuit;
            Panel?.Flush();
            preview.Clear();
            overlay?.Dispose();
        }
    }
}
