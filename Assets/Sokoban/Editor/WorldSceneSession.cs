/**
 * @file WorldSceneSession.cs
 * @brief 编辑态 Game 场景持有一份草稿、视角和模型投影。Tick 随活动场景建立或释放预览并刷新草稿；
 * OpenSession 优先还原磁盘现场，首次打开装配默认世界。Frame 按房间外接框定位，Solo 与 ReturnToWorld 切换可见范围。
 * ResetView 在换文件后恢复取景；Release 在关闭场景、进入运行态或域重载时保存并清理；
 * SceneGUI 把 Scene View 的格点击和手柄交给共享命令，保持 Unity 的原生导航。Refresh 刷新工具和视图。
 * PlayState 在域重载前传递草稿并保存单房取景，RestoreTrialView 在返回时还原视角。
 */
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Sokoban.Editor
{
    [InitializeOnLoad]
    internal static class WorldSceneSession
    {
        internal const string GamePath = "Assets/Scenes/Game.unity";
        internal static bool Active => !EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool("Sokoban.TrialTransition", false) && SceneManager.GetActiveScene().path == GamePath;
        internal static LayoutPanel Panel { get; private set; }
        static readonly RoomPreview preview = new RoomPreview();
        static readonly SceneGhost ghost = new SceneGhost();
        static bool dragging, rightDown, rightDragged, viewPending;
        static Vector2 rightStart;
        static Vector3 handle;
        static string soloId;
        static Vector3 previousPivot;
        static Quaternion previousRotation;
        static bool previousOrtho;
        static float previousSize;
        internal static bool IsSolo => soloId != null;

        static WorldSceneSession()
        {
            EditorApplication.update += Tick;
            SceneView.duringSceneGui += SceneGUI;
            AssemblyReloadEvents.beforeAssemblyReload += Release;
            EditorSceneManager.sceneClosing += (scene, removing) => { if (scene.path == GamePath) Release(); };
            EditorApplication.playModeStateChanged += PlayState;
            EditorApplication.focusChanged += focused => { if (!focused) Panel?.Flush(); };
            EditorApplication.quitting += Release;
        }

        [System.Serializable]
        sealed class TrialView
        {
            public string solo;
            public Vector3 pivot;
            public Quaternion rotation;
            public float size;
            public bool ortho;
        }

        static void PlayState(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                SessionState.EraseBool("Sokoban.TrialTransition");
                SessionState.EraseString("Sokoban.GameTrial");
            }
            if (state != PlayModeStateChange.ExitingEditMode || SceneManager.GetActiveScene().path != GamePath) return;
            if (Panel == null) OpenSession(true);
            var view = SceneView.lastActiveSceneView;
            if (view && !IsSolo) Panel.TrackView(view);
            try
            {
                PuzzleRun.FromDraft(Panel.Draft);
                if (!Panel.Flush()) throw new System.IO.IOException(Panel.Status);
                SessionState.SetString("Sokoban.GameTrial", Panel.Draft.Snapshot().ToString());
                if (view)
                    SessionState.SetString("Sokoban.TrialView", JsonUtility.ToJson(new TrialView {
                        solo = soloId, pivot = view.pivot, rotation = view.rotation, size = view.size, ortho = view.orthographic }));
                SessionState.SetBool("Sokoban.TrialTransition", true);
                Release();
            }
            catch (System.Exception error)
            {
                EditorApplication.isPlaying = false;
                Panel.Status = "无法开始试玩：" + error.Message;
                Debug.LogWarning(Panel.Status);
                Refresh();
            }
        }

        static void RestoreTrialView(SceneView view)
        {
            string json = SessionState.GetString("Sokoban.TrialView", "");
            if (json.Length == 0) return;
            var saved = JsonUtility.FromJson<TrialView>(json);
            previousPivot = Panel.Pivot;
            previousRotation = Panel.Rotation;
            previousSize = Panel.Zoom;
            previousOrtho = Panel.Ortho;
            soloId = saved.solo;
            view.LookAtDirect(saved.pivot, saved.rotation, saved.size);
            view.orthographic = saved.ortho;
            SessionState.EraseString("Sokoban.TrialView");
        }

        internal static void OpenSession(bool enteringPlay = false)
        {
            if (Panel != null || (!Active && !enteringPlay)) return;
            string testSlot = Application.isBatchMode ? System.Environment.GetEnvironmentVariable("SOKOBAN_EDITOR_TEST_SLOT") : null;
            Panel = new LayoutPanel("unity-editor",testSlot);
            if (!Panel.RecoverySlotExists)
            {
                string file = Path.Combine(Application.streamingAssetsPath, "World", "main.world.json");
                Panel.Run(() => Panel.Draft.Open(file, true), "已装配默认世界");
            }
            Panel.DocumentOpened += ResetView;
            viewPending = true;
        }

        internal static void Tick()
        {
            if (!Active)
            {
                if (EditorApplication.isPlaying || SceneManager.GetActiveScene().path != GamePath) Release();
                return;
            }
            OpenSession();
            var view = SceneView.lastActiveSceneView;
            if (view && viewPending)
            {
                if (Panel.HasView) Panel.RestoreView(view); else Frame(view, false);
                RestoreTrialView(view);
                viewPending = false;
            }
            if (soloId != null && (Panel.Draft.Placement == null || Panel.Draft.ActiveId != soloId)) ReturnToWorld();
            preview.Sync(Panel.Draft, soloId);
            if (preview.Settling && view) view.Repaint();
            if (view && !IsSolo) Panel.TrackView(view);
            Panel.Tick();
        }

        static void ResetView()
        {
            Panel.CancelTool();
            soloId = null;
            dragging = false;
            if (SceneView.lastActiveSceneView) Frame(SceneView.lastActiveSceneView, false);
            Refresh();
        }

        internal static void Release()
        {
            if (Panel == null) return;
            Panel.DocumentOpened -= ResetView;
            Panel.Flush();
            Panel = null;
            preview.Clear();
            ghost.Dispose();
            soloId = null;
            dragging = rightDown = rightDragged = false;
        }

        internal static void FocusRoom() => Frame(SceneView.lastActiveSceneView, true);

        internal static void FrameWorld()
        {
            if (IsSolo) ReturnToWorld();
            Frame(SceneView.lastActiveSceneView, false);
        }

        static void Frame(SceneView view, bool single)
        {
            if (!view || Panel == null) return;
            var draft = Panel.Draft;
            var min = draft.Origin;
            var max = min + RoomFile.Size(draft.Room) - Vector2Int.one;
            if (!single && draft.World != null && draft.World["rooms"].HasValues)
            {
                bool first = true;
                foreach (var placement in draft.World["rooms"])
                {
                    var origin = RoomFile.Cell(placement["origin"]);
                    var room = (Newtonsoft.Json.Linq.JObject)draft.Catalog[(string)placement["levelId"]];
                    var end = origin + RoomFile.Size(room) - Vector2Int.one;
                    min = first ? origin : Vector2Int.Min(min, origin);
                    max = first ? end : Vector2Int.Max(max, end);
                    first = false;
                }
            }
            var center = (Vector2)(min + max) * .5f;
            var rotation = single ? view.rotation : Quaternion.Euler(65, 0, 0);
            view.LookAtDirect(new Vector3(center.x,0,center.y), rotation, Mathf.Max(3, (max-min+Vector2Int.one).magnitude * .58f));
            if (!single) view.orthographic = true;
            view.Repaint();
        }

        internal static void Solo()
        {
            var view = SceneView.lastActiveSceneView;
            if (!view || Panel.Draft.Placement == null) return;
            if (!IsSolo)
            {
                previousPivot = view.pivot;
                previousRotation = view.rotation;
                previousSize = view.size;
                previousOrtho = view.orthographic;
                Panel.TrackView(view);
            }
            soloId = Panel.Draft.ActiveId;
            Frame(view,true);
            Refresh();
        }

        internal static void ReturnToWorld()
        {
            if (!IsSolo) return;
            soloId = null;
            var view = SceneView.lastActiveSceneView;
            if (view)
            {
                view.LookAtDirect(previousPivot, previousRotation, previousSize);
                view.orthographic = previousOrtho;
            }
            Refresh();
        }

        static void Refresh()
        {
            RoomWindow.Current?.Repaint();
            SceneView.RepaintAll();
        }

        static void SceneGUI(SceneView view)
        {
            if(!Active || Panel == null || Panel.Modal) { ghost.Hide(); return; }
            
            var ev=Event.current; var draft=Panel.Draft;
            if(ev.rawType==EventType.MouseDown && ev.button==1) {rightDown=true;rightDragged=false;rightStart=ev.mousePosition;}
            if(rightDown && ev.rawType==EventType.MouseDrag && (ev.delta.sqrMagnitude>=16 || Vector2.Distance(rightStart,ev.mousePosition)>=4)) rightDragged=true;
            bool cancelRight=ev.rawType==EventType.MouseUp && ev.button==1 && rightDown && !rightDragged && Vector2.Distance(rightStart,ev.mousePosition)<4;
            if(ev.rawType==EventType.MouseUp && ev.button==1) rightDown=false;
            if(!ev.alt && (cancelRight || ev.type==EventType.KeyDown && ev.keyCode==KeyCode.Escape)) {
                if(draft.Tool!=RoomTool.Select || dragging || Panel.Painting || Panel.PlacingRoom) {
                    bool ownsDrag = dragging || Panel.Painting;
                    Panel.CancelTool(); preview.HidePlacement(); ghost.Hide(); dragging=false;
                    if(ownsDrag) GUIUtility.hotControl=0;
                    if(!cancelRight) ev.Use();
                    view.Repaint();Refresh();
                }
                return;
            }
            if(ev.alt || Tools.current==Tool.View || rightDown || ev.type==EventType.ScrollWheel || ev.button==2) { ghost.Hide(); return; }
            if(ev.type==EventType.MouseMove || ev.type==EventType.MouseLeaveWindow) view.Repaint();
            bool hit=RoomPreview.CellFromRay(HandleUtility.GUIPointToWorldRay(ev.mousePosition),out var world);
            if (Panel.PlacingRoom && draft.World != null)
            {
                ghost.Hide();
                int id = GUIUtility.GetControlID(FocusType.Passive);
                if (ev.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
                if (ev.type == EventType.Repaint && hit && EditorWindow.mouseOverWindow == view)
                {
                    var color = Panel.Candidate(world, out var reason);
                    preview.ShowPlacement(draft, world, color);
                    SceneGhost.Mark(world, color, reason ?? "放置原点 " + world);
                }
                if (hit && ev.type == EventType.MouseDown && ev.button == 0)
                {
                    Panel.ConfirmRoom(world);
                    if (!Panel.PlacingRoom) preview.HidePlacement();
                    ev.Use();
                    Refresh();
                }
                if (ev.type == EventType.MouseLeaveWindow) preview.HidePlacement();
                return;
            }
            preview.HidePlacement();
            var origin=draft.Origin; var local=world-origin;
            bool roomVisible = draft.World == null || draft.Placement != null;
            var obj = roomVisible ? draft.Find(draft.Selected) : null;
            if(obj!=null && draft.Tool==RoomTool.Select && (obj.Parent==draft.Room["boxes"] || obj.Parent==draft.Room["targets"] || obj.Parent==draft.Room["decorations"])) {
                var cell=RoomFile.At(obj)+origin;
                if(!dragging) handle=new Vector3(cell.x,.78f,cell.y);
                bool release=ev.rawType==EventType.MouseUp && ev.button==0;
                EditorGUI.BeginChangeCheck(); Handles.color=Color.cyan;
                handle=Handles.Slider2D(handle,Vector3.up,Vector3.right,Vector3.forward,.20f,Handles.RectangleHandleCap,Vector2.zero);
                if(EditorGUI.EndChangeCheck()) dragging=true;
                if(dragging) {world=new Vector2Int(Mathf.RoundToInt(handle.x),Mathf.RoundToInt(handle.z));local=world-origin;}
                if(dragging && release) { dragging=false; Panel.Run(()=>draft.Move(local),"移动已提交");Refresh();view.Repaint();return; }
            }
            if(ev.type==EventType.Repaint) {
                foreach (var gap in RoomFile.BoundaryGaps(draft.Room)) SceneGhost.Mark(origin+gap, Color.red, "边界缺墙");
                if(obj!=null) SceneGhost.Mark(RoomFile.At(obj)+origin,Color.yellow,draft.Tool==RoomTool.Select && !dragging?draft.Selected+"  "+(RoomFile.At(obj)+origin):"");
                bool showGhost=false;
                if(hit && EditorWindow.mouseOverWindow==view) {
                    bool moving=dragging || draft.Tool==RoomTool.Move;
                    if(moving || draft.Tool==RoomTool.Box || draft.Tool==RoomTool.Target || draft.Tool==RoomTool.Decoration) {
                        bool box=draft.Tool==RoomTool.Box || moving && obj?.Parent==draft.Room["boxes"];
                        string issue=moving && (obj==null || !box && obj.Parent!=draft.Room["targets"] && obj.Parent!=draft.Room["decorations"])?"请选择箱子或目标":draft.PlacementError(box,local,moving?draft.Selected:null);
                        if(draft.World!=null && draft.Placement==null) issue="当前房间尚未放入世界";
                        Vector2Int? source=moving && obj!=null ? (Vector2Int?)(RoomFile.At(obj)+origin) : null;
                        if(source!=world && (!moving || obj!=null)) { ghost.Draw(draft,world,moving,issue,source);showGhost=true; }
                    } else if(obj==null || RoomFile.At(obj)+origin!=world) SceneGhost.Mark(world,Color.white,world.ToString());
                }
                if(!showGhost) ghost.Hide();
                Panel.DrawStroke();
            }
            int control=GUIUtility.GetControlID(FocusType.Passive);
            if(ev.type==EventType.Layout) HandleUtility.AddDefaultControl(control);
            if(ev.type==EventType.MouseDown && ev.button==0 && hit && HandleUtility.nearestControl==control) {
                if(IsSolo && !Panel.IsBrush && !RoomFile.Inside(draft.Room,local)) { ev.Use(); return; } if(Panel.IsBrush) {Panel.StartStroke(world);GUIUtility.hotControl=control;} else Panel.ClickWorld(world);
                ev.Use();Refresh();view.Repaint();
            } else if(Panel.Painting && ev.type==EventType.MouseDrag && hit) {Panel.ExtendStroke(world);ev.Use();view.Repaint();}
            else if(Panel.Painting && ev.rawType==EventType.MouseUp && ev.button==0) {Panel.EndStroke();GUIUtility.hotControl=0;ev.Use();Refresh();view.Repaint();}
        }
    }
}
