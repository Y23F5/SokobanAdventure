/**
 * @file RoomWindow.cs
 * @brief 房间布局窗口持有工具面板的窗口状态。Open 切换到 Game 并打开工具；
 * OnGUI 调用共享文件、命令及属性入口，定位、整图取景和单独查看委托场景会话；
 * DrawFunctionObjects 将带行为的装饰存入当前房间的功能物件目录，并列出可放置的定义；
 * ModelButton 用预览缓存显示实际模型缩略图，并让编辑器在首张图生成后刷新；
 * RequestQuit 保护未保存的修改，CloseApproved 结束本次显式退出。
 * 本文件读取 RoomDraft 的 Core 类型化模型：资源列表取自 Model.Assets，对象模板区取自 Model.Templates，主题取自 Model.Meta，不再有 JObject 访问。
 */
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sokoban.Editor
{
    public sealed class RoomWindow : EditorWindow
    {
        internal static RoomWindow Current { get; private set; }
        public LayoutPanel Panel => WorldSceneSession.Panel;
        BehaviorPage behavior;
        ObjectStateEditor stateEditor;
        AssetThumbnail thumbnails;
        bool closeApproved;
        Vector2 panelScroll;
        string definitionName = "";
        int category;

        bool ModelButton(string title, string model, bool builtin)
        {
            var room = Panel.Draft.Model.Assets;
            var preview = thumbnails.Get(room, model, builtin);
            if (!preview.IsCompleted) Repaint();
            var thumbnail = preview.Status == System.Threading.Tasks.TaskStatus.RanToCompletion ? preview.Result : null;
            GUILayout.BeginHorizontal();
            GUILayout.Label(thumbnail, GUILayout.Width(38), GUILayout.Height(38));
            string issue = builtin ? null : thumbnails.ModelIssue(room, model);
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
            if (draft.Model.Templates.Count == 0) return;
            GUILayout.Label("对象模板", EditorStyles.boldLabel);
            foreach (var entry in System.Linq.Enumerable.ToList(draft.Model.Templates))
            {
                string id = entry.Key;
                if (ModelButton("放置 · " + entry.Value.Name, entry.Value.Appearance?.Id, false))
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
            bool checking = Panel.Mode.Current == Sokoban.Core.UiMode.Check;
            if (checking)
            {
                GUILayout.Label("结构检查", EditorStyles.boldLabel);
                if (Panel.Issues.Count == 0) GUILayout.Label("没有结构问题");
                foreach (var issue in Panel.Issues)
                    if (GUILayout.Button("[" + issue.RuleId + "] " + issue.Message))
                    {
                        Panel.LocateIssue(issue);
                        WorldSceneSession.FocusRoom();
                        break;
                    }
                if (GUILayout.Button("返回布局")) Panel.CloseCheck();
            }
            else if (stateEditor != null)
            {
                if (!stateEditor.Draw(Panel)) stateEditor = null;
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
                if (Panel.Draft.CanEditStateProfile && GUILayout.Button("配置物件状态"))
                    stateEditor = new ObjectStateEditor(Panel.Draft);
                if (Panel.Draft.SelectedCell.HasValue && Panel.Draft.Room.TerrainAt(Panel.Draft.SelectedCell.Value).CategoryId != Sokoban.Core.TerrainCategories.Void && GUILayout.Button("编辑行为"))
                    OpenBehavior();
                DrawFunctionObjects();
                DrawAppearanceCatalog();
                GUILayout.Label("自定义资源");
                GUILayout.BeginHorizontal();
                foreach(var label in new[]{"导入模型","导入动作","导入贴图","导入界面图"})
                    if(GUILayout.Button(label)) { Panel.CancelTool(); Panel.ChooseFile(label); }
                GUILayout.EndHorizontal();
                foreach(var entry in Panel.Draft.Model.Assets)
                    {
                        GUILayout.BeginHorizontal();
                        var thumb=thumbnails.Get(Panel.Draft.Model.Assets,entry.Id);
                        GUILayout.Label(thumb.Status==System.Threading.Tasks.TaskStatus.RanToCompletion ? thumb.Result : null,GUILayout.Width(38),GUILayout.Height(38));
                        if(!thumb.IsCompleted) Repaint();
                        GUILayout.Label(entry.Name + "\n" + thumbnails.SizeReport(entry.Id));
                        string issue = thumbnails.ModelIssue(Panel.Draft.Model.Assets, entry.Id);
                        if(entry.Kind==Sokoban.Core.AssetKinds.Model && GUILayout.Button("放置"))
                        {
                            if (issue != null) Panel.Status = issue;
                            else Panel.Run(()=>Panel.Draft.SelectAsset(entry.Id),"在场景中选择放置格");
                        }
                        using (new EditorGUI.DisabledScope(!Panel.Draft.CanBindAsset(entry.Id) || issue != null))
                            if (GUILayout.Button(new GUIContent("绑定", issue ?? Panel.Draft.AssetBindingTarget(entry.Id))))
                                Panel.Run(() => { Panel.Draft.BindAsset(entry.Id); Panel.CancelTool(); }, "已绑定素材");
                        GUILayout.EndHorizontal();
                    }
            }
            if (!checking)
            {
                EditorGUILayout.HelpBox(Panel.Status, MessageType.Info);
                if (WorldSceneSession.AppearanceIssue is string appearanceIssue) EditorGUILayout.HelpBox(appearanceIssue, MessageType.Warning);
                if (Panel.StructuralIssue != null) EditorGUILayout.HelpBox(Panel.StructuralIssue, MessageType.Error);
            }
            GUILayout.EndScrollView();
            if (GUI.changed) SceneView.RepaintAll();
        }

        static System.Collections.Generic.List<string> ModelAssetIds(RoomDraft draft)
        {
            var ids = new System.Collections.Generic.List<string>();
            foreach (var asset in draft.Model.Assets)
                if (asset.Kind == Sokoban.Core.AssetKinds.Model) ids.Add(asset.Id);
            return ids;
        }

        void DrawAppearanceCatalog()
        {
            var draft = Panel.Draft;
            GUILayout.Label("素材库", EditorStyles.boldLabel);
            category = GUILayout.SelectionGrid(category, new[] { "箱子", "目标点", "墙壁", "地板", "其他" }, 3);
            var type = new[] { RoomTool.Box, RoomTool.Target, RoomTool.Wall, RoomTool.Floor, RoomTool.Decoration }[category];
            string categoryId = new[] { Sokoban.Core.ObjectCategories.Box, Sokoban.Core.ObjectCategories.Target, Sokoban.Core.TerrainCategories.Wall, Sokoban.Core.TerrainCategories.Floor, AppearanceLibrary.Decoration }[category];
            if (GUILayout.Button("导入" + AppearanceLibrary.TypeName(categoryId) + "模型"))
            {
                Panel.CancelTool();
                draft.Tool = type;
                Panel.ChooseFile("导入模型");
            }
            foreach (var entry in AppearanceLibrary.Entries(categoryId, draft.Model.Meta.ThemeId))
            {
                GUILayout.BeginHorizontal();
                if (ModelButton(entry.Name, entry.Prefab(0), true))
                    Panel.Run(() => draft.ChooseAppearance(entry.Id, categoryId), "已选 " + entry.Name + "，在场景中放置");
                using (new EditorGUI.DisabledScope(!draft.CanReplaceAppearance(entry.Id)))
                    if (GUILayout.Button("替换", GUILayout.Width(46))) Panel.Run(() => draft.ReplaceAppearance(entry.Id), "已替换所选外观");
                GUILayout.EndHorizontal();
            }
            foreach (string id in ModelAssetIds(draft))
            {
                GUILayout.BeginHorizontal();
                if (ModelButton(draft.AssetDefinition(id).Name, id, false))
                    Panel.Run(() => draft.SelectAsset(id, categoryId), "已选模型，在场景中放置");
                using (new EditorGUI.DisabledScope(!draft.CanBindAsset(id, categoryId) || thumbnails.ModelIssue(draft.Model.Assets, id) != null))
                    if (GUILayout.Button("替换", GUILayout.Width(46))) Panel.Run(() => draft.BindAsset(id, categoryId), "已替换所选外观");
                GUILayout.EndHorizontal();
            }
        }
    }
}
