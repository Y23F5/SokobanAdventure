/**
 * @file RoomWindow.cs
 * @brief 房间布局窗口持有工具面板的窗口状态。Open 切换到 Game 并打开工具；
 * OnGUI 调用共享文件、命令及属性入口，定位、整图取景和单独查看委托场景会话；
 * RequestQuit 保护未保存的修改，CloseApproved 结束本次显式退出。
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
        AssetThumbnail thumbnails;
        bool closeApproved;
        Vector2 panelScroll;

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

        void OnGUI()
        {
            if (!WorldSceneSession.Active) { GUILayout.Label("在 Game 场景的编辑模式下使用"); return; }
            WorldSceneSession.OpenSession();
            panelScroll = GUILayout.BeginScrollView(panelScroll);
            if (behavior != null)
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
                if (Panel.Draft.SelectedCell is Vector2Int cell && RoomFile.Terrain(Panel.Draft.Room, cell) != RoomFile.Void && GUILayout.Button("编辑行为"))
                    behavior = BehaviorPage.Open(Panel.Draft);
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
                        if((string)entry.Value["kind"]=="model" && GUILayout.Button("放置"))
                            Panel.Run(()=>Panel.Draft.SelectAsset(entry.Name),"在场景中选择放置格");
                        if(GUILayout.Button("绑定")) Panel.Run(()=>Panel.Draft.BindAsset(entry.Name),"已绑定素材");
                        GUILayout.EndHorizontal();
                    }
            }
            EditorGUILayout.HelpBox(Panel.Status, MessageType.Info);
            if (Panel.StructuralIssue != null) EditorGUILayout.HelpBox(Panel.StructuralIssue, MessageType.Error);
            GUILayout.EndScrollView();
            if (GUI.changed) SceneView.RepaintAll();
        }
    }
}
