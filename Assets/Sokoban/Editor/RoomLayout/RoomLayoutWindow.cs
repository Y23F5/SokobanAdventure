/**
 * @file RoomLayoutWindow.cs
 * @brief Unity 房间布局窗口。显示共享草稿、结构问题与文件确认，Scene View 会话负责地图输入与投影。
 * Open、CreateGUI：打开 Game 与 Scene View，从 UXML/USS 创建可停靠的分栏及交互入口。
 * OnEnable、OnDisable、GuardQuit：管理视图资源、保留草稿及拦截有未保存修改的退出。
 * Update、Bind、RefreshDocument：从当前会话刷新草稿投影，保留窗口分页和滚动。
 * BuildToolbars、DrawWorkspace、DrawInspector：连接共享文件与编辑命令，显示房间、选中项和世界信息。
 * DrawPalette、AddCard、RefreshThumbnails：按分页列出实际素材，复用模型预检和缩略图。
 * DrawIssues、Locate：显示结构问题，按 Core 定位结果选中对应格或实体。
 * RefreshDialog、对象状态分区承载 ObjectStateEditor，按选中实体应用状态表。
 * OnKey、StartTrial：将窗口快捷键转成共享命令，通过 Game 的 Play Mode 试玩当前草稿。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sokoban.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Sokoban.Editor
{
    public sealed class RoomLayoutWindow : EditorWindow
    {
        const string AssetFolder = "Assets/Sokoban/Editor/RoomLayout/";
        const string GameScene = "Assets/Scenes/Game.unity";
        const string Title = "房间布局";

        [SerializeField] string tab = PaletteTabs.Object;
        [SerializeField] string modelCategory = ObjectCategories.Box;
        static bool quitApproved;
        internal static RoomLayoutWindow Current { get; private set; }
        public LayoutPanel Panel => WorldSceneSession.Panel;
        internal bool BlocksSceneInput => Panel?.Modal == true;

        LayoutPanel bound;
        RoomDraftModel shownModel;
        int revision = -1;
        RoomTool shownTool;
        string shownBrush;
        string shownTemplate;
        string thumbnailRoom;
        string definitionName = "";
        string shownOperation;
        bool shownConfirming, shownKeep, shownDialog;
        string[] shownEntries;
        AssetThumbnail thumbnails;
        readonly List<(Image Image, Task<Texture2D> Task)> pendingThumbnails = new List<(Image, Task<Texture2D>)>();
        bool thumbnailsChanged;
        ObjectStateEditor stateEditor;
        VisualElement view;

        [MenuItem("Tools/Sokoban/房间布局")]
        public static void ShowLayout() => Open();

        public static RoomLayoutWindow Open()
        {
            if (SceneManager.GetActiveScene().path != GameScene)
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return null;
                EditorSceneManager.OpenScene(GameScene);
            }

            WorldSceneSession.OpenSession();
            GetWindow<SceneView>();
            var window = GetWindow<RoomLayoutWindow>(Title, true);
            window.Focus();
            return window;
        }

        [InitializeOnLoadMethod]
        static void InstallQuitGuard() => EditorApplication.wantsToQuit += GuardQuit;

        static bool GuardQuit()
        {
            var panel = WorldSceneSession.Panel;
            if (Application.isBatchMode || quitApproved || panel == null) return true;
            if (!panel.Draft.Dirty)
            {
                if (panel.Flush()) return true;
                Open();
                return false;
            }

            var window = Current ? Current : Open();
            if (!window) return false;
            window.Focus();
            panel.RequestExit(() =>
            {
                EditorApplication.delayCall += () =>
                {
                    quitApproved = true;
                    try { EditorApplication.Exit(0); }
                    finally { quitApproved = false; }
                };
            });
            return false;
        }

        void OnEnable()
        {
            Current = this;
            titleContent = new GUIContent(Title);
            minSize = new Vector2(680, 420);
        }

        void OnDisable()
        {
            Panel?.Flush();
            Bind(null);
            if (Current == this) Current = null;
        }

        public void CreateGUI()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AssetFolder + "RoomLayoutWindow.uxml");
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(AssetFolder + "RoomLayoutWindow.uss");
            rootVisualElement.Clear();
            if (!tree || !style)
            {
                view = null;
                rootVisualElement.Add(new HelpBox("房间布局缺少 UXML 或 USS，请检查 RoomLayout 目录。", HelpBoxMessageType.Error));
                return;
            }

            tree.CloneTree(rootVisualElement);
            view = rootVisualElement.Q("room-layout");
            view.styleSheets.Add(style);
            view.AddToClassList(EditorGUIUtility.isProSkin ? "sl-dark" : "sl-light");
            view.RegisterCallback<KeyDownEvent>(OnKey, TrickleDown.TrickleDown);
            view.Q<Button>("parent").clicked += () => Panel.ParentDirectory();
            view.Q<TextField>("filename").RegisterValueChangedCallback(evt => Panel.Filename = evt.newValue);
            BuildToolbars();
            revision = -1;
            shownEntries = null;
            shownDialog = false;
            Update();
        }

        void Update()
        {
            if (view == null) return;
            bool active = WorldSceneSession.Active;
            if (active) WorldSceneSession.OpenSession();
            Bind(active ? Panel : null);
            Show(view.Q("inactive"), bound == null);
            Show(view.Q("workbench"), bound != null);
            if (bound != null) RefreshDocument();
            RefreshDialog();
            view.Q("workbench").SetEnabled(bound != null && !bound.Modal);
        }

        void Bind(LayoutPanel panel)
        {
            if (ReferenceEquals(bound, panel)) return;
            if (bound != null) bound.DocumentOpened -= DocumentOpened;
            bound = panel;
            if (bound != null) bound.DocumentOpened += DocumentOpened;
            DocumentOpened();
        }

        void DocumentOpened()
        {
            shownModel = null;
            revision = -1;
            shownEntries = null;
            thumbnailRoom = null;
            thumbnails?.Dispose();
            thumbnails = bound == null ? null : new AssetThumbnail();
            pendingThumbnails.Clear();
            stateEditor = null;
        }

        void RefreshDocument()
        {
            var draft = bound.Draft;
            if (thumbnailRoom != draft.ActiveId)
            {
                if (thumbnailRoom != null)
                {
                    thumbnails.Dispose();
                    thumbnails = new AssetThumbnail();
                    pendingThumbnails.Clear();
                    stateEditor = null;
                }
                thumbnailRoom = draft.ActiveId;
            }

            RefreshThumbnails();
            string brush = draft.BrushAppearance?.Id;
            if (!ReferenceEquals(shownModel, draft.Model) || revision != draft.Revision ||
                shownTool != draft.Tool || shownBrush != brush || shownTemplate != draft.BrushTemplate)
            {
                shownModel = draft.Model;
                revision = draft.Revision;
                shownTool = draft.Tool;
                shownBrush = brush;
                shownTemplate = draft.BrushTemplate;
                DrawWorkspace(draft);
                DrawInspector(draft);
                DrawIssues();
                DrawPalette(draft);
            }

            view.Q<Button>("grid").text = WorldSceneSession.GridVisible ? "隐藏网格" : "显示网格";
            view.Q<Button>("solo").text = WorldSceneSession.IsSolo ? "返回世界" : "单独查看";
            view.Q<Button>("solo").SetEnabled(draft.Placement != null);
            view.Q<Button>("publish").SetEnabled(draft.World != null);
            view.Q<Button>("export-room").SetEnabled(draft.World != null);
            foreach (var button in view.Q("tools").Children().OfType<Button>())
                button.EnableInClassList("sl-active", button.name == "tool-" + draft.Tool);
            view.Q<Label>("status").text = ToolLabel(draft.Tool) + " · 悬停 " +
                (WorldSceneSession.HoverCell?.ToString() ?? "—") + " · " + bound.Status;
        }

        void BuildToolbars()
        {
            var files = view.Q("files");
            void Separator()
            {
                var line = new VisualElement();
                line.AddToClassList("sl-sep");
                files.Add(line);
            }

            AddButton(files, "新建空白", () => Panel.NewRoom(true));
            AddButton(files, "默认布局", () => Panel.NewDefaultRoom());
            foreach (string operation in new[] { "打开", "保存", "导入", "导出" })
            {
                var button = AddButton(files, operation, () => Panel.ChooseFile(operation));
                if (operation == "保存") button.AddToClassList("sl-primary");
            }
            Separator();
            var exportRoom = AddButton(files, "导出单房及素材", () => Panel.ChooseFile("导出单房"));
            exportRoom.name = "export-room";
            exportRoom.tooltip = "需要先把本房加入世界。";
            var publish = AddButton(files, "发布世界", () => Panel.ChooseFile("发布"));
            publish.name = "publish";
            publish.tooltip = "需要先加入世界。";
            Separator();
            AddButton(files, "撤销", () => History(false));
            AddButton(files, "重做", () => History(true));
            AddButton(files, "检查", () => { DrawIssues(); view.Q<ScrollView>("issues").Focus(); });
            Separator();
            AddButton(files, "试玩", StartTrial).AddToClassList("sl-primary");
            Separator();
            AddButton(files, "隐藏网格", () => WorldSceneSession.GridVisible = !WorldSceneSession.GridVisible).name = "grid";
            AddButton(files, "定位房间", WorldSceneSession.FocusRoom);
            var solo = AddButton(files, "单独查看", () =>
            {
                if (WorldSceneSession.IsSolo) WorldSceneSession.ReturnToWorld();
                else WorldSceneSession.Solo();
            });
            solo.name = "solo";
            solo.tooltip = "需要先把本房放进世界。";
            AddButton(files, "显示整图", WorldSceneSession.FrameWorld);
            var spacer = new VisualElement();
            spacer.AddToClassList("sl-spacer");
            files.Add(spacer);
            AddButton(files, "退出房间布局", () => Panel.Exit(() =>
            {
                WorldSceneSession.Release();
                Close();
            })).AddToClassList("sl-exit");

            var tools = view.Q("tools");
            foreach (RoomTool tool in new[] { RoomTool.Select, RoomTool.Move, RoomTool.Erase })
            {
                var button = AddButton(tools, ToolLabel(tool), () => UseTool(tool));
                button.name = "tool-" + tool;
            }
            foreach (string page in PaletteTabs.All)
                AddButton(view.Q("tabs"), PaletteTabs.Name(page), () => { tab = page; DrawPalette(Panel.Draft); }).name = "tab-" + page;
        }

        void DrawWorkspace(RoomDraft draft)
        {
            var area = view.Q<ScrollView>("workspace");
            area.Clear();
            Heading(area, "Scene View");
            Line(area, "在 Scene View 选格、画地形和放置。短按右键或 Esc 取消工具。");
            Line(area, "[ / ] 旋转 · Ctrl+Z / Ctrl+Y 撤销与重做");
            Heading(area, draft.World == null ? "独立房间" : "世界 · " + draft.World.Rooms.Count + " 个房间");
            if (draft.World == null)
            {
                AddButton(area, "以此房间创建世界", () => Panel.Run(() => { draft.BeginWorld(); Panel.Flush(); }, "请设置房间放置原点"));
                return;
            }

            foreach (var room in draft.Catalog)
            {
                string id = room.Key;
                AddButton(area, (draft.ActiveId == id ? "● " : "") + room.Value.Meta.Name + " · " + id, () => draft.UseRoom(id));
            }
            foreach (var connection in draft.World.Connections)
            {
                AddButton(area, Panel.ConnectionAction(connection), () => Panel.ToggleConnection(connection));
                AddButton(area, "解除 " + connection.From.LevelId + " → " + connection.To.LevelId,
                    () => Panel.Run(() => draft.RemoveConnection(connection.Id), "已解除门连接"));
            }
            DrawConnectionForm(area, draft);
            AddButton(area, "从文件加入房间", () => Panel.ChooseFile("加入房间"));
            Line(area, draft.Placement == null ? "尚未放置" : "房间原点 " + draft.Origin);
            AddButton(area, draft.Placement == null ? "在地图放置整房" : "在地图移动整房", Panel.StartRoomPlacement);
            Coordinates(area, draft.Origin, "应用世界坐标", cell =>
                Panel.Run(() => draft.PlaceRoom(cell, draft.Placement != null), "已更新房间原点"));
            if (draft.Placement != null)
                AddButton(area, "从世界移出房间", () => Panel.Run(draft.RemoveRoom, "房间已保留在目录中"));
            AddButton(area, "检查世界", () =>
            {
                var issues = WorldLayout.Issues(draft.World, draft.Catalog).ToArray();
                Panel.Status = issues.Length == 0 ? "放置与直连检查通过" : string.Join("；", issues);
            });
        }

        void DrawConnectionForm(VisualElement area, RoomDraft draft)
        {
            var endpoints = draft.World.Rooms.SelectMany(room => draft.Catalog[room.LevelId].Doors.Select(door =>
                new WorldEndpoint(room.LevelId, door.Id))).ToList();
            if (endpoints.Select(endpoint => endpoint.LevelId).Distinct().Count() < 2) return;
            Heading(area, "添加门连接");
            var indexes = Enumerable.Range(0, endpoints.Count).ToList();
            string Label(int index) => endpoints[index].LevelId + " / " + endpoints[index].DoorId;
            int firstOtherRoom = endpoints.FindIndex(endpoint => endpoint.LevelId != endpoints[0].LevelId);
            var from = new PopupField<int>("起点", indexes, 0, Label, Label);
            var to = new PopupField<int>("终点", indexes, firstOtherRoom, Label, Label);
            var modes = new List<string> { WorldCodec.Direct, WorldCodec.Teleport };
            string ModeLabel(string value) => value == WorldCodec.Direct ? "直连" : "传送";
            var mode = new PopupField<string>("方式", modes, 0, ModeLabel, ModeLabel);
            var allowReturn = new Toggle("允许回退") { value = true };
            area.Add(from);
            area.Add(to);
            area.Add(mode);
            area.Add(allowReturn);
            AddButton(area, "添加连接", () => Panel.Run(
                () => draft.AddConnection(endpoints[from.value], endpoints[to.value], mode.value, allowReturn.value), "已添加门连接"));
        }

        void DrawInspector(RoomDraft draft)
        {
            var area = view.Q<ScrollView>("inspector");
            area.Clear();
            Heading(area, "房间");
            var size = Line(area, draft.Room.Width + "×" + draft.Room.Height);
            size.tooltip = draft.ActiveId;
            var name = new TextField("名称") { isDelayed = true, value = draft.Model.Meta.Name };
            name.RegisterValueChangedCallback(evt =>
            {
                Panel.Run(() => draft.Rename(evt.newValue), "已更新名称");
                name.SetValueWithoutNotify(draft.Model.Meta.Name);
            });
            area.Add(name);
            var description = new TextField("说明") { isDelayed = true, multiline = true, value = draft.Model.Meta.Description };
            var entryHint = new TextField("进房提示") { isDelayed = true, multiline = true, value = draft.Model.Meta.EntryHint };
            var playHint = new TextField("玩法提示") { isDelayed = true, multiline = true, value = draft.Model.Meta.PlayHint };
            foreach (var field in new[] { description, entryHint, playHint })
            {
                field.RegisterValueChangedCallback(_ => Panel.Run(
                    () => draft.SetRoomText(description.value, entryHint.value, playHint.value), "已更新房间文本"));
                area.Add(field);
            }
            Heading(area, "背景主题");
            var themes = Row(area);
            foreach (string themeId in AppearanceLibrary.Themes)
            {
                string chosen = themeId;
                var themeButton = AddButton(themes, AppearanceLibrary.ThemeName(chosen), () => Panel.Run(() => draft.SetTheme(chosen), "已切换主题"));
                themeButton.EnableInClassList("sl-active", draft.Theme == chosen);
                themeButton.EnableInClassList("sl-primary", draft.Theme == chosen);
            }
            Line(area, draft.Model.RecoveryDevice == null ? "恢复装置：缺少，请在素材库「其他」页放置" : "恢复装置：" + DeviceKinds.First(kindEntry => kindEntry.kind == draft.Model.RecoveryDevice.Kind).name + " · " + draft.Model.RecoveryDevice.Duration.ToString("0.0") + " 秒");

            if (!draft.SelectedCell.HasValue) return;
            Cell cell = draft.SelectedCell.Value;
            TerrainCell terrain = draft.Room.TerrainAt(cell);
            var item = draft.Find(draft.Selected);
            var decoration = draft.FindDecoration(draft.Selected);
            var door = draft.Model.Doors.FirstOrDefault(entry => entry.Cell == cell);
            bool multiple = draft.Selection.Count > 1;
            if (!multiple && item?.CategoryId == ObjectCategories.Hint)
            {
                var text = new TextField("提示文字") { value = item.HintText, multiline = true, isDelayed = true };
                text.RegisterValueChangedCallback(evt => Panel.Run(() => draft.SetHintText(item.Id, evt.newValue), "已更新提示文字"));
                area.Add(text);
            }
            Heading(area, multiple ? "多选 · " + draft.Selection.Count + " 格" :
                door != null ? "门" : item != null || decoration != null ? "对象" : "地形格");

            if (multiple)
            {
                Line(area, "地形 · " + Mixed(draft.Selection.Select(point => draft.Room.TerrainAt(point).CategoryId)));
                Line(area, "对象类别 · " + Mixed(draft.Selection.Select(point => CategoryAt(draft, point) ?? "—")));
                Line(area, "外观 · " + Mixed(draft.Selection.Select(point => AppearanceAt(draft, point))));
            }
            else
            {
                Line(area, "格 · " + cell);
                Line(area, "地形 · " + terrain.CategoryId + (terrain.State == null ? "" : " · " + terrain.State));
                if (door != null)
                {
                    Line(area, "ID · " + door.Id + " · " + door.Role);
                    Line(area, "朝向 · " + door.Outward);
                    if (door.Role == "entry")
                    {
                        if (draft.Model.PrimaryEntranceId == door.Id) Line(area, "主入口");
                        else AddButton(area, "设为主入口", () => Panel.Run(() => draft.SetPrimaryEntrance(door.Id), "已设置主入口"));
                    }
                    if (draft.World != null)
                        foreach (var link in draft.World.Connections.Where(link =>
                            (link.From.LevelId == draft.ActiveId && link.From.DoorId == door.Id) ||
                            (link.To.LevelId == draft.ActiveId && link.To.DoorId == door.Id)))
                            AddButton(area, Panel.ConnectionAction(link), () => Panel.ToggleConnection(link));
                }
                else if (item != null || decoration != null)
                {
                    Line(area, "ID · " + (item?.Id ?? decoration.Id));
                    Line(area, "类别 · " + (item?.CategoryId ?? AppearanceLibrary.Decoration));
                    Coordinates(area, cell + draft.Origin, "移动至世界坐标", point =>
                        Panel.Run(() => draft.Move(point - draft.Origin), "已移动对象"));
                }
                Line(area, "外观 · " + AppearanceAt(draft, cell));
                var look = item?.Appearance ?? decoration?.Appearance ?? draft.Room.AppearanceAt(cell);
                var asset = draft.AssetDefinition(look?.Id);
                if (asset != null)
                {
                    var height = new FloatField("高度偏移（格）") { value = asset.HeightOffset, isDelayed = true };
                    height.RegisterValueChangedCallback(evt => Panel.Run(() => draft.SetAssetHeight(asset.Id, evt.newValue), "已更新素材高度"));
                    area.Add(height);
                }
            }

            if (terrain.CategoryId != TerrainCategories.Void)
            {
                var rotations = Row(area);
                AddButton(rotations, "左转 90°", () => Rotate(-1)).tooltip = "快捷键 [";
                AddButton(rotations, "右转 90°", () => Rotate(1)).tooltip = "快捷键 ]";
                AddButton(area, "编辑行为", BehaviorGraphWindow.Open);
            }
            if (!multiple)
            {
                if (draft.CanEditStateProfile)
                {
                    var stateSection = new Foldout { text = "对象状态", value = false };
                    stateSection.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue) stateEditor = new ObjectStateEditor(draft);
                        else stateEditor = null;
                    });
                    stateSection.Add(new IMGUIContainer(() =>
                    {
                        if (stateSection.value && stateEditor != null && !stateEditor.Draw(bound)) stateSection.value = false;
                    }));
                    area.Add(stateSection);
                }
                if (draft.BoxSelected)
                    for (int i = 0; i < RoomDraft.BuiltinBoxes.Length; i++)
                    {
                        string model = RoomDraft.BuiltinBoxes[i];
                        AddButton(area, "绑定内置模型 · " + RoomDraft.BuiltinBoxNames[i],
                            () => Panel.Run(() => draft.BindBuiltinBox(model), "已绑定箱体模型"));
                    }
                if (draft.CanDefineObject)
                {
                    var field = new TextField("功能物件名称") { value = definitionName };
                    field.RegisterValueChangedCallback(evt => definitionName = evt.newValue);
                    area.Add(field);
                    AddButton(area, "将装饰存为功能物件", () => Panel.Run(() =>
                        draft.DefineObject(definitionName), "已添加功能物件"));
                }
            }
        }

        static string CategoryAt(RoomDraft draft, Cell cell) =>
            draft.Room.ObjectAt(cell)?.CategoryId ??
            (draft.Model.Decorations.Any(entry => entry.Cell == cell) ? AppearanceLibrary.Decoration : null);

        static string AppearanceAt(RoomDraft draft, Cell cell)
        {
            var look = draft.Room.ObjectAt(cell)?.Appearance ??
                draft.Model.Decorations.FirstOrDefault(entry => entry.Cell == cell)?.Appearance ??
                draft.Room.AppearanceAt(cell);
            return look == null ? "主题默认" : look.Id + " · 变体 " + look.Variant;
        }

        static string Mixed(IEnumerable<string> values)
        {
            var distinct = values.Distinct().Take(2).ToArray();
            return distinct.Length == 1 ? distinct[0] : "不同值";
        }


        void DrawPalette(RoomDraft draft)
        {
            var area = view.Q<ScrollView>("palette");
            area.Clear();
            pendingThumbnails.Clear();
            AddButton(area, "导入素材包", () => Import("导入素材包", null));
            foreach (string page in PaletteTabs.All)
                view.Q<Button>("tab-" + page).EnableInClassList("sl-active", page == tab);

            if (tab == PaletteTabs.Other)
            {
                AddCard(area, draft, "提示物件", null, false, () => UseTool(RoomTool.Hint), draft.Tool == RoomTool.Hint);
                AddCard(area, draft, "入口（主入口会自动生成出生点）", "LayoutModels/Door", true, () => UseTool(RoomTool.Entry), draft.Tool == RoomTool.Entry);
                AddCard(area, draft, "出口", "LayoutModels/Door", true, () => UseTool(RoomTool.Exit), draft.Tool == RoomTool.Exit);
                DrawRecoveryDevice(area, draft);
            }

            string groupKey = null;
            VisualElement host = area;
            var paletteItems = draft.PaletteItems(tab);
            foreach (var item in paletteItems)
            {
                if (item.AssetId != null && item.TemplateId == null && tab == PaletteTabs.Custom) continue;
                string nextGroup = item.Theme == null || item.TemplateId != null ? null : tab + "/" + item.Theme + "/" + item.CategoryId;
                if (nextGroup != groupKey)
                {
                    host = area;
                    if (nextGroup != null)
                    {
                        int variants = paletteItems.Count(other => other.Theme == item.Theme && other.CategoryId == item.CategoryId && other.TemplateId == null);
                        var fold = new Foldout { text = AppearanceLibrary.ThemeName(item.Theme) + " · " + AppearanceLibrary.TypeName(item.CategoryId) + "（" + variants + " 种）" + (item.Theme == draft.Theme ? " · 当前主题" : "") };
                        fold.SetValueWithoutNotify(openGroups.TryGetValue(nextGroup, out bool open) ? open : item.Theme == draft.Theme);
                        string key = nextGroup;
                        fold.RegisterValueChangedCallback(evt => { if (evt.target == fold) openGroups[key] = evt.newValue; });
                        area.Add(fold);
                        host = fold;
                        RoomTool? randomTool = ToolForCategory(item.CategoryId);
                        if (item.Theme != null && (randomTool == RoomTool.Floor || randomTool == RoomTool.Wall))
                        {
                            var random = AddButton(host, "随机变体", () => UseTool(randomTool.Value));
                            random.tooltip = "不指定外观，按房间与格坐标在该主题的变体中固定随机";
                        }
                    }
                }
                groupKey = nextGroup;
                RoomTool? tool = ToolForCategory(item.CategoryId);
                Action choose = null;
                if (item.TemplateId != null)
                    choose = () =>
                    {
                        string error = item.AssetId == null ? null : thumbnails.ModelIssue(draft.Model.Assets, item.AssetId);
                        if (error != null) { Panel.ReportIssue(error); return; }
                        UseTool(RoomTool.Decoration);
                        Panel.Run(() => draft.SelectDefinition(item.TemplateId), "已选功能物件");
                    };
                else if (tool.HasValue)
                    choose = () =>
                    {
                        if (item.AssetId != null)
                            ChooseImported(draft, item.AssetId, item.CategoryId);
                        else
                        {
                            UseTool(tool.Value);
                            Panel.Run(() => draft.ChooseAppearance(item.Id, item.CategoryId), "已选 " + item.Name);
                        }
                    };

                string preview = item.AssetId ?? item.Prefab;
                var card = AddCard(host, draft, item.Name, preview, item.AssetId == null, choose,
                    item.TemplateId != null ? draft.BrushTemplate == item.TemplateId : draft.BrushAppearance?.Id == item.Id);
                if (item.TemplateId == null && item.AssetId == null && draft.CanReplaceAppearance(item.Id))
                    AddButton(card, "替换", () => Panel.Run(() => draft.ReplaceAppearance(item.Id), "已替换所选外观")).AddToClassList("sl-small");
                else if (item.TemplateId == null && item.AssetId != null && draft.CanBindAsset(item.AssetId, item.CategoryId))
                    AddButton(card, "替换", () => BindImported(draft, item.AssetId, item.CategoryId)).AddToClassList("sl-small");
                if (item.TemplateId == null && item.AssetId != null)
                    ModelSize(host, item.AssetId);
            }

            if (tab == PaletteTabs.Custom)
            {
                Heading(area, "自定义资源");
                foreach (string operation in new[] { "导入模型", "导入动作", "导入贴图", "导入界面图" })
                    AddButton(area, operation, () => Import(operation, null));

                var categories = new List<string>
                {
                    ObjectCategories.Box, ObjectCategories.Target,
                    TerrainCategories.Wall, TerrainCategories.Floor, AppearanceLibrary.Decoration, ObjectCategories.Custom
                };
                var dropdown = new PopupField<string>("模型用途", categories, Math.Max(0, categories.IndexOf(modelCategory)),
                    AppearanceLibrary.TypeName, AppearanceLibrary.TypeName);
                dropdown.RegisterValueChangedCallback(evt => { modelCategory = evt.newValue; DrawPalette(draft); });
                area.Add(dropdown);
                AddButton(area, "按所选用途导入模型", () => Import("导入模型", ToolForCategory(modelCategory)));

                foreach (var asset in draft.Model.Assets.Where(entry => !draft.PackParts().Any(part => part.asset.Id == entry.Id)))
                {
                    bool model = asset.Kind == AssetKinds.Model;
                    Action choose = model ? () => ChooseImported(draft, asset.Id, modelCategory) : (Action)null;
                    var card = AddCard(area, draft, asset.Name, asset.Id, false, choose, draft.BrushAsset == asset.Id);
                    var bind = AddButton(card, "绑定", () => BindImported(draft, asset.Id, null));
                    bind.tooltip = draft.AssetBindingTarget(asset.Id);
                    bind.SetEnabled(draft.CanBindAsset(asset.Id));
                    if (model) ModelSize(area, asset.Id);
                    else if (asset.Kind != AssetKinds.UiImage) Line(area, asset.Kind);
                }
            }
            else if (tab != PaletteTabs.Other)
            {
                string[] categories = tab == PaletteTabs.Terrain
                    ? new[] { TerrainCategories.Floor, TerrainCategories.Wall }
                    : tab == PaletteTabs.Object
                        ? new[] { ObjectCategories.Box, ObjectCategories.Target }
                        : new[] { AppearanceLibrary.Decoration };
                foreach (string category in categories)
                    AddButton(area, "导入" + AppearanceLibrary.TypeName(category) + "模型",
                        () => Import("导入模型", ToolForCategory(category)));
            }
        }

        static readonly (RecoveryDeviceKind kind, string name, string script, string theme)[] DeviceKinds =
        {
            (RecoveryDeviceKind.Forklift, "叉车", "warehouse-lift", "warehouse"),
            (RecoveryDeviceKind.Arm, "机械臂", "factory-belt", "factory"),
            (RecoveryDeviceKind.Tree, "树木", "forest-decay", "forest")
        };

        static readonly (string name, int rotation)[] DeviceSides = { ("北侧", 2), ("东侧", 3), ("南侧", 0), ("西侧", 1) };

        RecoveryDeviceKind deviceKind = RecoveryDeviceKind.Forklift;

        readonly Dictionary<string, bool> openGroups = new Dictionary<string, bool>(StringComparer.Ordinal);

        void DrawRecoveryDevice(VisualElement area, RoomDraft draft)
        {
            Heading(area, "恢复装置");
            var device = draft.Model.RecoveryDevice;
            if (device != null) deviceKind = device.Kind;
            Line(area, device == null ? "本房还没有恢复装置。先选类型，再选放在哪一侧。"
                : DeviceKinds.First(entry => entry.kind == device.Kind).name + " · 墙外 " + device.Anchor + " · " + EntryName(device.Rotation));
            var kinds = Row(area);
            foreach (var entry in DeviceKinds)
            {
                var chosen = entry;
                var button = AddButton(kinds, entry.name, () =>
                {
                    deviceKind = chosen.kind;
                    if (device != null && device.Kind != chosen.kind) PlaceDevice(draft, device.Rotation);
                    else DrawPalette(draft);
                });
                button.EnableInClassList("sl-active", deviceKind == entry.kind);
                button.EnableInClassList("sl-primary", deviceKind == entry.kind);
            }
            var sides = Row(area);
            foreach (var entry in DeviceSides)
            {
                int rotation = entry.rotation;
                AddButton(sides, entry.name, () => PlaceDevice(draft, rotation)).tooltip = "把装置放在房间" + entry.name + "墙外正中，朝房内";
            }
            if (device == null) return;
            var actions = Row(area);
            AddButton(actions, "左转 90°", () => Panel.Run(() => draft.RotateRecoveryDevice(-1), "已旋转恢复装置")).tooltip = "快捷键 [";
            AddButton(actions, "右转 90°", () => Panel.Run(() => draft.RotateRecoveryDevice(1), "已旋转恢复装置")).tooltip = "快捷键 ]";
            AddButton(actions, "删除", () => Panel.Run(draft.DeleteRecoveryDevice, "已删除恢复装置")).AddToClassList("sl-exit");
        }

        static string EntryName(int rotation) => rotation switch { 0 => "向北进房", 1 => "向东进房", 2 => "向南进房", _ => "向西进房" };

        void PlaceDevice(RoomDraft draft, int rotation)
        {
            var room = draft.Room;
            var anchor = rotation switch
            {
                0 => new Cell(room.Width / 2, -1),
                1 => new Cell(-1, room.Height / 2),
                2 => new Cell(room.Width / 2, room.Height),
                _ => new Cell(room.Width, room.Height / 2)
            };
            var existing = draft.Model.RecoveryDevice;
            var kind = DeviceKinds.First(entry => entry.kind == deviceKind);
            if (existing != null && existing.Kind == deviceKind)
            {
                Panel.Run(() =>
                {
                    draft.MoveRecoveryDevice(anchor);
                    if (existing.Rotation != rotation) draft.RotateRecoveryDevice(rotation - existing.Rotation);
                }, "已移动恢复装置");
                return;
            }
            if (existing != null && !EditorUtility.DisplayDialog("替换恢复装置", "每个房间只能有一个恢复装置。用" + kind.name + "替换现有装置吗？", "替换", "取消"))
                return;
            var device = new RecoveryDevice(existing?.Id ?? "device-" + Guid.NewGuid().ToString("N").Substring(0, 8), kind.kind, anchor, rotation,
                kind.script, existing?.Duration ?? 3.7f, existing?.Params, existing?.ItemName, existing?.ItemDescription, existing?.ItemAcquiredHint);
            Panel.Run(() =>
            {
                draft.PlaceRecoveryDevice(device, existing != null);
                if (draft.Theme != kind.theme) draft.SetTheme(kind.theme);
            }, "已放置" + kind.name);
        }

        void ModelSize(VisualElement area, string id)
        {
            var draft = Panel.Draft;
            string issue = thumbnails.ModelIssue(draft.Model.Assets, id, !draft.PackParts().Any(part => part.asset.Id == id));
            string size = thumbnails.SizeReport(id);
            if (issue != null && issue != "正在检查模型尺寸" && !string.IsNullOrEmpty(size)) Line(area, size);
        }

        void Import(string operation, RoomTool? tool)
        {
            Panel.CancelTool();
            if (tool.HasValue) Panel.Draft.Tool = tool.Value;
            Panel.ChooseFile(operation);
        }

        void ChooseImported(RoomDraft draft, string id, string category)
        {
            string error = thumbnails.ModelIssue(draft.Model.Assets, id, !draft.PackParts().Any(part => part.asset.Id == id));
            if (error != null) { Panel.ReportIssue(error); return; }
            UseTool(ToolForCategory(category) ?? RoomTool.Decoration);
            Panel.Run(() => draft.ChooseAppearance(id, category), "已选模型，在场景中放置");
        }

        void BindImported(RoomDraft draft, string id, string category)
        {
            string error = thumbnails.ModelIssue(draft.Model.Assets, id, !draft.PackParts().Any(part => part.asset.Id == id));
            if (error != null) { Panel.ReportIssue(error); return; }
            Panel.Run(() => { draft.BindAsset(id, category); Panel.CancelTool(); }, "已绑定素材");
        }

        VisualElement AddCard(VisualElement parent, RoomDraft draft, string title, string thumbnail,
            bool builtin, Action choose, bool selected)
        {
            var row = new VisualElement();
            row.AddToClassList("sl-card");
            row.EnableInClassList("sl-active", selected);
            parent.Add(row);
            if (!string.IsNullOrEmpty(thumbnail))
            {
                var image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.AddToClassList("sl-thumb");
                row.Add(image);
                var task = thumbnails.Get(draft.Model.Assets, thumbnail, builtin);
                if (task.Status == TaskStatus.RanToCompletion) image.image = task.Result;
                else if (!task.IsCompleted) pendingThumbnails.Add((image, task));
            }
            VisualElement name = choose == null ? (VisualElement)Line(row, title) : AddButton(row, title, choose);
            name.AddToClassList("sl-card-main");
            return row;
        }

        void RefreshThumbnails()
        {
            for (int i = pendingThumbnails.Count - 1; i >= 0; i--)
            {
                var work = pendingThumbnails[i];
                if (!work.Task.IsCompleted) continue;
                if (work.Task.Status == TaskStatus.RanToCompletion) work.Image.image = work.Task.Result;
                pendingThumbnails.RemoveAt(i);
                thumbnailsChanged = true;
            }
            if (thumbnailsChanged && pendingThumbnails.Count == 0)
            {
                thumbnailsChanged = false;
                DrawPalette(bound.Draft);
            }
        }

        void DrawIssues()
        {
            var area = view.Q<ScrollView>("issues");
            area.Clear();
            var issues = Panel.Issues;
            view.Q<Label>("issues-heading").text = "结构检查 · " + (issues.Count == 0 ? "没有问题" : issues.Count + " 项");
            foreach (var issue in issues)
            {
                string severity = issue.Severity switch { Severity.Error => "错误", Severity.Warning => "警告", _ => "提示" };
                var button = AddButton(area, severity + " · " + issue.Message + (string.IsNullOrEmpty(issue.Location) ? "" : " · " + issue.Location), () => Locate(issue));
                button.tooltip = issue.RuleId;
                button.AddToClassList("sl-entry");
            }
        }

        void Locate(Issue issue)
        {
            var draft = Panel.Draft;
            var target = IssueLocator.Locate(issue);
            Panel.LocateIssue(issue);
            if (target.Kind == LocateKind.Object)
            {
                var cell = draft.Find(target.Id)?.Cell ?? draft.FindDecoration(target.Id)?.Cell;
                if (cell.HasValue) draft.Select(cell.Value);
            }
            else if (target.Kind == LocateKind.Door)
            {
                var door = draft.Model.Doors.FirstOrDefault(entry => entry.Id == target.Id);
                if (door != null) draft.Select(door.Cell);
            }
            WorldSceneSession.FocusRoom();
        }

        void RefreshDialog()
        {
            var dialog = view.Q("dialog");
            bool visible = bound != null && bound.Modal;
            Show(dialog, visible);
            if (!visible) { shownDialog = false; return; }
            bool confirming = bound.Confirming;
            bool rebuildActions = !shownDialog || shownConfirming != confirming || shownKeep != bound.CanKeepDraft ||
                shownOperation != bound.Operation;
            bool enteringFile = !confirming && (!shownDialog || shownConfirming || shownOperation != bound.Operation);
            shownDialog = true;
            shownConfirming = confirming;
            shownKeep = bound.CanKeepDraft;
            shownOperation = bound.Operation;

            view.Q<Label>("dialog-title").text = confirming ? "当前房间有未保存的修改" : bound.DialogTitle;
            view.Q<Label>("dialog-note").text = confirming ? "" : bound.DialogNote;
            string warning = confirming ? null : bound.DialogWarning;
            view.Q<Label>("dialog-message").text = string.IsNullOrEmpty(warning) ? bound.Status : warning + "\n" + bound.Status;
            Show(view.Q("file-fields"), !confirming);
            if (rebuildActions)
            {
                var actions = view.Q("dialog-actions");
                actions.Clear();
                if (confirming)
                {
                    AddButton(actions, "保存并继续", bound.ContinueAfterSave);
                    AddButton(actions, "放弃修改并继续", bound.DiscardAndContinue);
                    if (bound.CanKeepDraft) AddButton(actions, "保留草稿退出", bound.KeepAndExit);
                }
                else AddButton(actions, bound.Operation, bound.SubmitFile).name = "submit-file";
                AddButton(actions, "取消", bound.CancelDialog);
                if (confirming) actions.Q<Button>()?.Focus();
            }
            if (confirming) return;

            view.Q<Button>("submit-file").text = bound.Overwrite ? "确认覆盖" : bound.Operation;
            view.Q<Label>("directory").text = bound.DirectoryPath;
            var filename = view.Q<TextField>("filename");
            if (filename.value != bound.Filename) filename.SetValueWithoutNotify(bound.Filename ?? "");
            if (enteringFile) filename.Focus();
            if (ReferenceEquals(shownEntries, bound.Entries)) return;
            shownEntries = bound.Entries;
            var entries = view.Q<ScrollView>("entries");
            entries.Clear();
            entries.scrollOffset = Vector2.zero;
            foreach (string entry in shownEntries ?? Array.Empty<string>())
            {
                var button = AddButton(entries, LayoutPanel.EntryIcon(entry) + " " + Path.GetFileName(entry), () => bound.PickEntry(entry));
                button.AddToClassList("sl-entry");
                button.tooltip = bound.EntryNote(entry) ?? entry;
            }
        }

        void StartTrial()
        {
            if (BlocksSceneInput || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (Panel.StructuralIssue is string issue)
            {
                Panel.ReportIssue(issue);
                DrawIssues();
                view.Q<ScrollView>("issues").Focus();
                return;
            }
            EditorApplication.isPlaying = true;
        }

        static DoorInfo PrimaryEntrance(RoomDraft draft) =>
            draft.Model.Doors.FirstOrDefault(door => door.Id == draft.Model.PrimaryEntranceId && door.Role == "entry");

        void UseTool(RoomTool tool)
        {
            if (tool == RoomTool.Spawn && PrimaryEntrance(Panel.Draft) == null)
            {
                Panel.ReportIssue("请先设置主入口。");
                return;
            }
            Panel.CancelTool();
            Panel.Draft.Tool = tool;
            Panel.Status = tool == RoomTool.Spawn ? "出生点固定在主入口内侧格，点击 Scene View 确认。" : "已选工具：" + ToolLabel(tool);
        }

        void Rotate(int turns) => Panel.Run(() => Panel.Draft.RotateSelection(turns), turns < 0 ? "已逆时针旋转" : "已顺时针旋转");

        void RotateShortcut(int turns)
        {
            var draft = Panel.Draft;
            if (draft.Tool == RoomTool.Select && !draft.SelectedCell.HasValue && draft.Model.RecoveryDevice != null)
                Panel.Run(() => draft.RotateRecoveryDevice(turns), "已旋转恢复装置");
            else if (draft.Tool == RoomTool.Select) Rotate(turns);
            else if (draft.Tool != RoomTool.Move && draft.Tool != RoomTool.Erase) draft.RotateBrush(turns);
        }

        void History(bool redo)
        {
            if (redo) Panel.Run(Panel.Draft.Redo, "已重做");
            else Panel.Run(Panel.Draft.Undo, "已撤销");
        }

        void OnKey(KeyDownEvent evt)
        {
            if (bound == null || !WorldSceneSession.Active) return;
            if (bound.Modal)
            {
                if (evt.keyCode == KeyCode.Escape) { bound.CancelDialog(); evt.StopPropagation(); }
                return;
            }
            if (BlocksSceneInput || evt.altKey || IsTextInput(evt.target as VisualElement)) return;
            if (evt.ctrlKey || evt.commandKey)
            {
                if (evt.keyCode != KeyCode.Z && evt.keyCode != KeyCode.Y) return;
                History(evt.keyCode == KeyCode.Y || evt.shiftKey);
            }
            else if (evt.keyCode == KeyCode.LeftBracket || evt.character == '[') RotateShortcut(-1);
            else if (evt.keyCode == KeyCode.RightBracket || evt.character == ']') RotateShortcut(1);
            else if (evt.keyCode == KeyCode.Escape) Panel.CancelTool();
            else return;
            evt.StopImmediatePropagation();
            evt.PreventDefault();
            SceneView.RepaintAll();
        }

        static bool IsTextInput(VisualElement element)
        {
            for (var parent = element; parent != null; parent = parent.parent)
                if (parent is TextField || parent is IntegerField) return true;
            return false;
        }

        static RoomTool? ToolForCategory(string category) => category switch
        {
            TerrainCategories.Floor => RoomTool.Floor,
            TerrainCategories.Wall => RoomTool.Wall,
            TerrainCategories.Openable => RoomTool.Vine,
            ObjectCategories.Box => RoomTool.Box,
            ObjectCategories.Target => RoomTool.Target,
            AppearanceLibrary.Decoration => RoomTool.Decoration,
            AssetPackManifest.Entrance => RoomTool.Entry,
            AssetPackManifest.Exit => RoomTool.Exit,
            _ => (RoomTool?)null
        };

        static string ToolLabel(RoomTool tool) => tool switch
        {
            RoomTool.Select => "选择",
            RoomTool.Floor => "画地板",
            RoomTool.Wall => "画墙",
            RoomTool.Erase => "擦除",
            RoomTool.Box => "放箱子",
            RoomTool.Target => "放目标",
            RoomTool.Move => "移动",
            RoomTool.Decoration => "放装饰",
            RoomTool.Hint => "放置提示物件",
            RoomTool.Entry => "放入口",
            RoomTool.Exit => "放出口",
            RoomTool.Spawn => "设出生点",
            _ => tool.ToString()
        };

        void Coordinates(VisualElement parent, Cell initial, string label, Action<Cell> apply)
        {
            var row = Row(parent);
            var x = new IntegerField("X") { value = initial.X };
            var y = new IntegerField("Y") { value = initial.Y };
            x.labelElement.style.minWidth = 14;
            y.labelElement.style.minWidth = 14;
            x.style.flexGrow = 1;
            y.style.flexGrow = 1;
            row.Add(x);
            row.Add(y);
            AddButton(parent, label, () => apply(new Cell(x.value, y.value)));
        }

        Button AddButton(VisualElement parent, string title, Action action)
        {
            var button = new Button(() => { action(); SceneView.RepaintAll(); }) { text = title };
            parent.Add(button);
            return button;
        }

        static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement();
            row.AddToClassList("sl-row");
            parent.Add(row);
            return row;
        }

        static Label Line(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.AddToClassList("sl-wrap");
            parent.Add(label);
            return label;
        }

        static void Heading(VisualElement parent, string text) => Line(parent, text).AddToClassList("sl-heading");

        static void Show(VisualElement element, bool visible) =>
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
