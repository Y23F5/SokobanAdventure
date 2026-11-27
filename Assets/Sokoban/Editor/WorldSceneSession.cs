/**
 * @file WorldSceneSession.cs
 * DrawGrid 在房间外扩六格绘制可切换网格；DrawSelectionRectangle 绘制框选范围；EditShortcut 与工作窗口调用同一草稿历史；DeleteAt 优先删除格中实体，再尝试擦除地形。
 * @brief 编辑态 Game 场景持有一份草稿、视角和模型投影。Tick 随活动场景建立或释放预览并刷新草稿；
 * OpenSession 优先还原磁盘现场，首次打开装配默认世界。Frame 按房间外接框定位，Solo 与 ReturnToWorld 切换可见范围。
 * ResetView 在换文件后恢复取景；Release 在关闭场景、进入运行态或域重载时保存并清理；
 * SceneGUI 按原生 Scene View 朝向裁切墙体，把格点击和手柄交给共享命令。Refresh 刷新工具和视图。
 * PlayState 在域重载前传递草稿并保存单房取景，RestoreTrialView 在返回时还原视角。
 * 本文件读取 RoomDraft 的 Core 类型化模型：C/V 在 Unity 的 Vector2Int 与 Core.Cell 之间转换，BoundaryGaps 取自 RoomValidator 的 boundary.open 规则，
 * 选中对象与装饰分别经 RoomDraft.Find 和 FindDecoration 读取，不再有 JObject 访问。
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
        static Vector2Int? rectangleStart;
        static InterfaceTheme theme;
        internal static Sokoban.Core.Cell? HoverCell { get; private set; }
        internal static bool GridVisible
        {
            get => SessionState.GetBool("Sokoban.LayoutGrid", true);
            set { SessionState.SetBool("Sokoban.LayoutGrid", value); Refresh(); }
        }

        static InterfaceTheme Theme => theme ? theme : theme = Resources.Load<InterfaceTheme>("LayoutUI/WarmPixel");
        internal const string GamePath = "Assets/Scenes/Game.unity";
        internal static bool Active => !EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool("Sokoban.TrialTransition", false) && SceneManager.GetActiveScene().path == GamePath;
        internal static LayoutPanel Panel { get; private set; }
        static readonly RoomPreview preview = new RoomPreview();
        internal static string AppearanceIssue => preview.AppearanceIssue;
        internal static string PreviewDoorId => preview.PreviewDoor;
        internal static float PreviewDoorOpen => preview.PreviewOpen;

        internal static void SetDoorPreview(string doorId, float open)
        {
            preview.PreviewDoor = doorId;
            preview.PreviewOpen = open;
            SceneView.RepaintAll();
        }
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
        static Sokoban.Core.Cell C(Vector2Int v) => new Sokoban.Core.Cell(v.x, v.y);
        static Vector2Int V(Sokoban.Core.Cell c) => new Vector2Int(c.X, c.Y);

        static System.Collections.Generic.IEnumerable<Vector2Int> BoundaryGaps(RoomDraft draft)
        {
            foreach (var issue in RoomFile.Issues(draft.Model, Sokoban.Core.ValidationStage.Publish))
            {
                if (issue.RuleId != "boundary.open") continue;
                var parts = issue.Location.Trim('(', ')').Split(',');
                yield return new Vector2Int(int.Parse(parts[0]), int.Parse(parts[1]));
            }
        }

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
            public RoomTool tool;
            public string appearance;
            public int variant;
            public string category;
            public string template;
            public int brushRotation;
            public bool hasSelection;
            public Vector2Int primary;
            public Vector2Int[] selection;
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
                SessionState.SetString("Sokoban.GameTrial", Panel.Draft.Snapshot());
                if (view)
                    SessionState.SetString("Sokoban.TrialView", JsonUtility.ToJson(new TrialView {
                        solo = soloId, pivot = view.pivot, rotation = view.rotation, size = view.size, ortho = view.orthographic,
                        tool = Panel.Draft.Tool, appearance = Panel.Draft.BrushAppearance?.Id,
                        variant = Panel.Draft.BrushAppearance?.Variant ?? 0, category = Panel.Draft.BrushCategory,
                        template = Panel.Draft.BrushTemplate, brushRotation = Panel.Draft.BrushRotation,
                        hasSelection = Panel.Draft.SelectedCell.HasValue, primary = Panel.Draft.SelectedCell.HasValue ? V(Panel.Draft.SelectedCell.Value) : default,
                        selection = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(Panel.Draft.Selection, V))
                    }));
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
            var draft = Panel.Draft;
            if (saved.hasSelection)
            {
                var primary = C(saved.primary);
                draft.Select(primary);
                if (saved.selection != null)
                    foreach (var selected in saved.selection)
                        if (selected != saved.primary) draft.Selection.Add(C(selected));
            }
            draft.Tool = saved.tool;
            if (!string.IsNullOrEmpty(saved.template)) draft.SelectDefinition(saved.template);
            else if (!string.IsNullOrEmpty(saved.appearance) && !string.IsNullOrEmpty(saved.category))
                draft.ChooseAppearance(saved.appearance, saved.category, saved.variant);
            draft.RotateBrush(saved.brushRotation);
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
            rectangleStart = null;
            HoverCell = null;
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
            var min = V(draft.Origin);
            var max = min + new Vector2Int(draft.Room.Width, draft.Room.Height) - Vector2Int.one;
            if (!single && draft.World != null && draft.World.Rooms.Count > 0)
            {
                bool first = true;
                foreach (var placement in draft.World.Rooms)
                {
                    var origin = V(placement.Origin);
                    var room = draft.Catalog[placement.LevelId].Room;
                    var end = origin + new Vector2Int(room.Width, room.Height) - Vector2Int.one;
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
            RoomLayoutWindow.Current?.Repaint();
            SceneView.RepaintAll();
        }

        static void DrawGrid(RoomDraft draft)
        {
            if (!GridVisible) return;
            const int margin = 6;
            float x = draft.Origin.X - margin - .5f;
            float z = draft.Origin.Y - margin - .5f;
            int width = draft.Room.Width + 2 * margin;
            int height = draft.Room.Height + 2 * margin;
            Color color = Theme ? Theme.hover : Color.white;
            color.a *= .36f;
            using (new Handles.DrawingScope(color))
            {
                for (int i = 0; i <= width; i++)
                    Handles.DrawLine(new Vector3(x + i, .024f, z), new Vector3(x + i, .024f, z + height));
                for (int i = 0; i <= height; i++)
                    Handles.DrawLine(new Vector3(x, .024f, z + i), new Vector3(x + width, .024f, z + i));
            }
        }

        static void DrawSelectionRectangle(Vector2Int start, Vector2Int end, Color color)
        {
            float x0 = Mathf.Min(start.x, end.x) - .5f, x1 = Mathf.Max(start.x, end.x) + .5f;
            float z0 = Mathf.Min(start.y, end.y) - .5f, z1 = Mathf.Max(start.y, end.y) + .5f;
            const float y = .08f;
            using (new Handles.DrawingScope(color))
                Handles.DrawAAPolyLine(3, new Vector3(x0, y, z0), new Vector3(x1, y, z0),
                    new Vector3(x1, y, z1), new Vector3(x0, y, z1), new Vector3(x0, y, z0));
        }

        static bool EditShortcut(Event ev, RoomDraft draft)
        {
            if (ev.type != EventType.KeyDown || ev.alt) return false;
            int turns = ev.keyCode == KeyCode.LeftBracket || ev.character == '[' ? -1
                : ev.keyCode == KeyCode.RightBracket || ev.character == ']' ? 1 : 0;
            if (turns != 0 && !ev.control && !ev.command)
            {
                if (draft.Tool == RoomTool.Select && !draft.SelectedCell.HasValue && draft.Model.RecoveryDevice != null)
                    Panel.Run(() => draft.RotateRecoveryDevice(turns), "已旋转恢复装置");
                else if (draft.Tool == RoomTool.Select) Panel.Run(() => draft.RotateSelection(turns), "已旋转所选内容");
                else if (draft.Tool != RoomTool.Move && draft.Tool != RoomTool.Erase) draft.RotateBrush(turns);
                return true;
            }
            if (!ev.control && !ev.command) return false;
            if (ev.keyCode == KeyCode.Z && !ev.shift) Panel.Run(draft.Undo, "已撤销");
            else if (ev.keyCode == KeyCode.Y || ev.keyCode == KeyCode.Z && ev.shift) Panel.Run(draft.Redo, "已重做");
            else return false;
            return true;
        }

        static void DeleteAt(RoomDraft draft, Sokoban.Core.Cell cell)
        {
            var id = draft.Model.Decorations.Find(item => item.Cell == cell)?.Id;
            if (id != null) Panel.Run(() => draft.RemoveObject(id), "已删除");
            else
            {
                var error = draft.Brush(new[] { cell }, RoomTool.Erase);
                Panel.Status = error ?? "已擦除地形";
            }
            Refresh();
        }

        static void SceneGUI(SceneView view)
        {
            if (!Active || Panel == null) { ghost.Hide(); HoverCell = null; return; }
            var ev = Event.current;
            var draft = Panel.Draft;
            if (ev.type == EventType.Repaint) preview.ClipWalls(draft, view.rotation);
            if (Panel.Modal || RoomLayoutWindow.Current && RoomLayoutWindow.Current.BlocksSceneInput) { ghost.Hide(); HoverCell = null; return; }

            if (EditShortcut(ev, draft)) { ev.Use(); Refresh(); return; }
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
                else if (cancelRight && Tools.current != Tool.View &&
                    (draft.World == null || draft.Placement != null) &&
                    RoomPreview.CellFromRay(HandleUtility.GUIPointToWorldRay(ev.mousePosition), out var clicked))
                {
                    var cell = C(clicked) - draft.Origin;
                    if (draft.Room.TerrainAt(cell).CategoryId != Sokoban.Core.TerrainCategories.Void || draft.Model.Decorations.Exists(item => item.Cell == cell))
                    {
                        if (!draft.Selection.Contains(cell)) draft.Select(cell);
                        var menu = new GenericMenu();
                        if (draft.Room.TerrainAt(cell).CategoryId != Sokoban.Core.TerrainCategories.Void)
                            menu.AddItem(new GUIContent("编辑行为"), false, BehaviorGraphWindow.Open);
                        menu.AddItem(new GUIContent("旋转 90°"), false, () => { Panel.Run(() => draft.RotateSelection(1), "已旋转"); Refresh(); });
                        menu.AddItem(new GUIContent("删除"), false, () => DeleteAt(draft, cell));
                        menu.ShowAsContext();
                        ev.Use();
                        Refresh();
                    }
                }
                return;
            }
            if (ev.type == EventType.Repaint) DrawGrid(draft);
            if (ev.type == EventType.MouseLeaveWindow) HoverCell = null;
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
                    SceneGhost.Mark(C(world), color, reason ?? "放置原点 " + world);
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
            var origin=V(draft.Origin); var local=world-origin;
            if (ev.type == EventType.Repaint) HoverCell = hit && EditorWindow.mouseOverWindow == view ? C(local) : (Sokoban.Core.Cell?)null;
            bool roomVisible = draft.World == null || draft.Placement != null;
            var obj = roomVisible ? draft.Find(draft.Selected) : null;
            var deco = roomVisible && obj == null ? draft.FindDecoration(draft.Selected) : null;
            Sokoban.Core.Cell? selAt = obj != null ? obj.Cell : deco?.Cell;
            bool draggable = obj != null ? obj.CategoryId != Sokoban.Core.ObjectCategories.Spawn : deco != null;
            if(selAt.HasValue && draggable && draft.Tool==RoomTool.Select) {
                var cell=V(selAt.Value)+origin;
                if(!dragging) handle=new Vector3(cell.x,.78f,cell.y);
                bool release=ev.rawType==EventType.MouseUp && ev.button==0;
                EditorGUI.BeginChangeCheck(); Handles.color=Color.cyan;
                handle=Handles.Slider2D(handle,Vector3.up,Vector3.right,Vector3.forward,.20f,Handles.RectangleHandleCap,Vector2.zero);
                if(EditorGUI.EndChangeCheck()) dragging=true;
                if(dragging) {world=new Vector2Int(Mathf.RoundToInt(handle.x),Mathf.RoundToInt(handle.z));local=world-origin;}
                if(dragging && release) { dragging=false; Panel.Run(()=>draft.Move(C(local)),"移动已提交");Refresh();view.Repaint();return; }
            }
            if(ev.type==EventType.Repaint) {
                foreach (var gap in BoundaryGaps(draft)) SceneGhost.Mark(C(origin+gap), Color.red, "边界缺墙");
                var selectedTint = Theme ? Theme.active : Color.yellow;
                foreach (var point in draft.Selection) SceneGhost.Mark(C(origin + V(point)), selectedTint, "");
                var primary = selAt ?? draft.SelectedCell;
                if (primary.HasValue) SceneGhost.Mark(C(V(primary.Value) + origin), selectedTint,
                    draft.Tool == RoomTool.Select && !dragging ? draft.Selected ?? "" : "");
                if (rectangleStart.HasValue && hit) DrawSelectionRectangle(origin + rectangleStart.Value, world, selectedTint);
                bool showGhost=false;
                if(hit && EditorWindow.mouseOverWindow==view) {
                    bool moving=dragging || draft.Tool==RoomTool.Move;
                    if (draft.Tool == RoomTool.Floor || draft.Tool == RoomTool.Wall)
                    {
                        ghost.Draw(draft, C(world), false,draft.TerrainPlacementError(C(local), draft.Tool), null);
                        showGhost = true;
                    }
                    else if (RoomDraft.TryMarkerTool(draft.Tool, out _))
                    {
                        ghost.Draw(draft, C(world), false, draft.PlacementMarkerError(C(local)), null);
                        showGhost = true;
                    }
                    else if(moving || draft.Tool==RoomTool.Box || draft.Tool==RoomTool.Target || draft.Tool==RoomTool.Decoration) {
                        bool box=draft.Tool==RoomTool.Box || moving && obj?.CategoryId==Sokoban.Core.ObjectCategories.Box;
                        string issue=moving && (!selAt.HasValue || !draggable)?"请选择箱子或目标":draft.PlacementError(box,C(local),moving?draft.Selected:null);
                        if (!moving && draft.Tool == RoomTool.Decoration)
                            issue = draft.BrushCategory == AppearanceLibrary.Decoration
                                ? draft.Room.Inside(C(local)) ? "装饰只能放在房间外" : null
                                : draft.PlacementError(Sokoban.Core.ObjectCategories.Custom, C(local));
                        if(draft.World!=null && draft.Placement==null) issue="当前房间尚未放入世界";
                        Sokoban.Core.Cell? source=moving && selAt.HasValue ? (Sokoban.Core.Cell?)C(V(selAt.Value)+origin) : null;
                        if(source!=C(world) && (!moving || selAt.HasValue)) { ghost.Draw(draft,C(world),moving,issue,source);showGhost=true; }
                    } else if(!selAt.HasValue || V(selAt.Value)+origin!=world) SceneGhost.Mark(C(world),Theme ? Theme.hover : Color.white,local.ToString());
                }
                if(!showGhost) ghost.Hide();
                Panel.DrawStroke();
            }
            int control=GUIUtility.GetControlID(FocusType.Passive);
            if(ev.type==EventType.Layout) HandleUtility.AddDefaultControl(control);
            if (rectangleStart.HasValue && ev.rawType == EventType.MouseUp && ev.button == 0)
            {
                if (hit) draft.SelectRectangle(C(rectangleStart.Value),C(local));
                rectangleStart=null; GUIUtility.hotControl=0; ev.Use(); Refresh(); view.Repaint(); return;
            }
            if(ev.type==EventType.MouseDown && ev.button==0 && hit && HandleUtility.nearestControl==control) {
                if (draft.Tool == RoomTool.Select && ev.clickCount == 2
                    && draft.Room.TerrainAt(C(local)).CategoryId != Sokoban.Core.TerrainCategories.Void)
                {
                    draft.Select(C(local));
                    BehaviorGraphWindow.Open();
                    ev.Use(); Refresh(); return;
                }
                if (draft.Tool == RoomTool.Select && ev.shift) { rectangleStart=local; GUIUtility.hotControl=control; ev.Use(); return; }
                if (draft.Tool == RoomTool.Select && (ev.control || ev.shift || ev.command)) { draft.Select(C(local), true); ev.Use(); Refresh(); view.Repaint(); return; }
                if(IsSolo && !Panel.IsBrush && !draft.Room.Inside(C(local))) { ev.Use(); return; } if(Panel.IsBrush) {Panel.StartStroke(world);GUIUtility.hotControl=control;} else Panel.ClickWorld(world);
                ev.Use();Refresh();view.Repaint();
            }
            else if (rectangleStart.HasValue && ev.type == EventType.MouseDrag) { ev.Use(); view.Repaint(); }
            else if(Panel.Painting && ev.type==EventType.MouseDrag && hit) {Panel.ExtendStroke(world);ev.Use();view.Repaint();}
            else if(Panel.Painting && ev.rawType==EventType.MouseUp && ev.button==0) {Panel.EndStroke();GUIUtility.hotControl=0;ev.Use();Refresh();view.Repaint();}
        }
    }
}
