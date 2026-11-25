/**
 * @file RoomWindow.cs
 * @brief 房间布局窗口持有工具面板的窗口状态。Open 切换到 Game 并打开工具；
 * OnGUI 调用共享文件、命令及属性入口，定位、整图取景和单独查看委托场景会话；
 * DrawFunctionObjects 将带行为的装饰存入当前房间的功能物件目录，并列出可放置的定义；
 * ModelButton 用预览缓存显示实际模型缩略图，并让编辑器在首张图生成后刷新；
 * RequestQuit 保护未保存的修改，CloseApproved 结束本次显式退出。
 */
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Newtonsoft.Json.Linq;

namespace Sokoban.Editor
{
    public sealed class RoomWindow : EditorWindow
    {
        internal static RoomWindow Current { get; private set; }
        public LayoutPanel Panel => WorldSceneSession.Panel;
        BehaviorPage behavior;
        AssetThumbnail thumbnails;
        bool closeApproved;
        Vector2 panelScroll;
        string definitionName = "";

        bool ModelButton(string title, string model, bool builtin)
        {
            var preview = thumbnails.Get(Panel.Draft.Room, model, builtin);
            if (!preview.IsCompleted) Repaint();
            var thumbnail = preview.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? preview.Result : null;
            GUILayout.BeginHorizontal();
            GUILayout.Label(thumbnail, GUILayout.Width(38), GUILayout.Height(38));
            string issue = builtin ? null : thumbnails.ModelIssue(Panel.Draft.Room, model);
            bool pressed = GUILayout.Button(new GUIContent(title, issue ?? "选取后在 Scene View 中吸附放置"), GUILayout.Height(38));
            GUILayout.EndHorizontal();
            if (pressed && issue != null) { Panel.Status = issue; return false; }
            return pressed;
        }

        void DrawFunctionObjects()
        {
            var draft = Panel.Draft;
            if (draft.CanDefineObject)
            {
                definitionName = EditorGUILayout.TextField("功能物件名称", definitionName);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(definitionName)))
                    if (GUILayout.Button("将所选装饰存为功能物件"))
                        Panel.Run(() => draft.DefineObject(definitionName), "已添加功能物件，可在两端放置");
            }
            if (!(draft.Room["objectDefinitions"] is JObject definitions) || definitions.Count == 0) return;
            GUILayout.Label("功能物件", EditorStyles.boldLabel);
            foreach (var entry in definitions.Properties())
            {
                string id = entry.Name;
                if (ModelButton("放置 · " + (string)entry.Value["name"], (string)entry.Value["visual"]["model"], false))
                    Panel.Run(() => draft.SelectDefinition(id), "在场景中选择放置格");
            }
        }

        [MenuItem("Tools/Sokoban/房间布局")]
        public static void ShowLayout() => Open();

        public static RoomWindow Open()
        {
            if (SceneManager.GetActiveScene().path != WorldSceneSession.GamePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
                EditorSceneManager.OpenScene(WorldSceneSession.GamePath);
            }
            WorldSceneSession.OpenSession();
            GetWindow<SceneView>();
            var window = GetWindow<RoomWindow>("房间布局", true, typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser"));
            window.Focus();
            return window;
        }

        void OnEnable()
        {
            thumbnails = new AssetThumbnail();
            Current = this;
            minSize = new Vector2(260, 240);
            EditorApplication.wantsToQuit += RequestQuit;
        }

        void OnDisable()
        {
            thumbnails?.Dispose();
            EditorApplication.wantsToQuit -= RequestQuit;
            Panel?.Flush();
            if (Current == this) Current = null;
        }

        bool RequestQuit()
        {
            if (Application.isBatchMode || closeApproved || Panel == null) return true;
            if (!Panel.Draft.Dirty) return Panel.Flush();
            Focus();
            Panel.RequestExit(() => {
                closeApproved = true;
                EditorApplication.delayCall += () => { EditorApplication.ExecuteMenuItem("File/Quit"); if (this) closeApproved = false; };
            });
            Repaint();
            return false;
        }

        void CloseApproved()
        {
            WorldSceneSession.Release();
            Close();
        }

        internal void OpenBehavior()
        {
            if (!Panel.Draft.SelectedCell.HasValue) return;
            behavior = BehaviorPage.Open(Panel.Draft);
            panelScroll = Vector2.zero;
            Focus();
            Repaint();
        }

        void OnGUI()
        {
            if (!WorldSceneSession.Active) { GUILayout.Label("在 Game 场景的编辑模式下使用"); return; }
            WorldSceneSession.OpenSession();
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            if (Panel.ShowingSolution)
            {
                GUILayout.Label("检查结果", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(Panel.SolutionText, EditorStyles.wordWrappedLabel,
                    GUILayout.Height(EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(Panel.SolutionText), Mathf.Max(100, position.width - 30)) + 8));
                if (GUILayout.Button("返回布局")) Panel.CloseSolution();
                Repaint();
            }
            else if (behavior != null)
            {
                if (!behavior.Draw(Panel)) behavior = null;
            }
            else if (Panel.Modal)
            {
                GUILayout.BeginVertical(GUILayout.Width(Mathf.Max(1, position.width - 20)));
                Panel.Dialog();
                GUILayout.EndVertical();
            }
            else
            {
                Panel.Files();
                if (GUILayout.Button("从格子图纸新建房间")) RoomBlueprint.Import(Panel);
                if(Panel.Draft.World!=null && GUILayout.Button("导出当前房间及素材")) Panel.ChooseFile("导出单房");
                if (GUILayout.Button("试玩当前房间")) EditorApplication.isPlaying = true;
                EditorGUILayout.Space();
                Panel.Tools();
                Panel.WorkspaceControls();
                if (Panel.Draft.Placement != null)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("定位房间")) WorldSceneSession.FocusRoom();
                    if (WorldSceneSession.IsSolo) { if (GUILayout.Button("返回世界")) WorldSceneSession.ReturnToWorld(); }
                    else if (GUILayout.Button("单独查看")) WorldSceneSession.Solo();
                    if (GUILayout.Button("显示整图")) WorldSceneSession.FrameWorld();
                    GUILayout.EndHorizontal();
                }
                if (GUILayout.Button("退出房间布局")) Panel.Exit(CloseApproved);
                EditorGUILayout.Space();
                Panel.Properties();
                if (Panel.Draft.SelectedCell is Vector2Int cell && RoomFile.Terrain(Panel.Draft.Room, cell) != RoomFile.Void && GUILayout.Button("编辑行为"))
                    OpenBehavior();
                DrawFunctionObjects();
                if (RoomDraft.IsAppearanceTool(Panel.Draft.Tool))
                {
                    var tool = Panel.Draft.Tool;
                    GUILayout.Label("当前工具的模型", EditorStyles.boldLabel);
                    if (ModelButton("使用默认外观", RoomPreview.BrushModel(tool, (string)Panel.Draft.Room["themeId"]), true)) Panel.Draft.Tool = tool;
                    var catalog = Panel.Draft.Room["appearanceCatalog"]?[tool.ToString().ToLowerInvariant()] as Newtonsoft.Json.Linq.JObject;
                    if (catalog != null) foreach (var look in catalog.Properties())
                    {
                        string id = (string)look.Value["model"];
                        if (ModelButton((string)Panel.Draft.Room["assets"]?[id]?["name"] ?? id, id, false))
                            Panel.Run(() => Panel.Draft.SelectAsset(id, tool), "已选模型，在场景中放置");
                    }
                    if (GUILayout.Button("向当前工具导入模型")) Panel.ChooseFile("导入模型");
                }
                GUILayout.Label("房间素材");
                GUILayout.BeginHorizontal();
                foreach(var label in new[]{"导入模型","导入动作","导入贴图","导入界面图"})
                    if(GUILayout.Button(label)) Panel.ChooseFile(label);
                GUILayout.EndHorizontal();
                if(Panel.Draft.Room["assets"] is Newtonsoft.Json.Linq.JObject assets)
                    foreach(var entry in assets.Properties())
                    {
                        GUILayout.BeginHorizontal();
                        var thumb=thumbnails.Get(Panel.Draft.Room,entry.Name);
                        GUILayout.Label(thumb.Status==System.Threading.Tasks.TaskStatus.RanToCompletion ? thumb.Result : null,GUILayout.Width(38),GUILayout.Height(38));
                        if(!thumb.IsCompleted) Repaint();
                        GUILayout.Label((string)entry.Value["name"] + "\n" + thumbnails.SizeReport(entry.Name));
                        string issue = thumbnails.ModelIssue(Panel.Draft.Room, entry.Name);
                        if((string)entry.Value["kind"]=="model" && GUILayout.Button("放置"))
                        {
                            if (issue != null) Panel.Status = issue;
                            else Panel.Run(()=>Panel.Draft.SelectAsset(entry.Name),"在场景中选择放置格");
                        }
                        using (new EditorGUI.DisabledScope(!Panel.Draft.CanBindAsset(entry.Name) || issue != null))
                            if (GUILayout.Button(new GUIContent("绑定", issue ?? Panel.Draft.AssetBindingTarget(entry.Name))))
                                Panel.Run(() => { Panel.Draft.BindAsset(entry.Name); Panel.CancelTool(); }, "已绑定素材");
                        GUILayout.EndHorizontal();
                    }
            }
            EditorGUILayout.HelpBox(Panel.Status, MessageType.Info);
            if (WorldSceneSession.AppearanceIssue is string appearanceIssue) EditorGUILayout.HelpBox(appearanceIssue, MessageType.Warning);
            if (Panel.StructuralIssue != null) EditorGUILayout.HelpBox(Panel.StructuralIssue, MessageType.Error);
            GUILayout.EndScrollView();
            if (GUI.changed) SceneView.RepaintAll();
        }
    }
}
