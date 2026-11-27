/**
 * @file RuntimeLayoutUI.cs
 * @brief Act 把场景按钮的操作交给布局会话；Tool 选择素材或移动工具，ApplyPoint 执行手动输入的世界格命令。
 * LateUpdate 刷新布局、文件及确认面板；RefreshEntries 按目录或房间清单生成可滚动按钮；
 * AddPaletteCard 生成带 44px thumb 底框缩略图的素材卡，新增模型用 card-dashed 虚线占位卡，返回卡显示返回加分类名。绑定状态和目标来自共享草稿命令。
 * BuildTabs 用同一个 Bookmark 预制件生成地形、物件、装饰、其他、自定义五个页签，tab 是素材区的唯一显隐依据。
 * BuildMust 与 RefreshMust 在左上角用代码构建 210 宽房间必填项清单，每行经 Tag 显示 tag-ok 或 tag-bad，状态来自 Core 校验结果，点击经 LayoutPanel.LocateIssue 定位。
 * BuildProps 与 RefreshProps 用 panel-small 构建属性卡：56px thumb 缩略图、标题、编号标签、移动与危险色删除按钮，删除调用 RoomDraft.RemoveObject，未选中对象时回到文字属性。
 * 检查模式只读 LayoutPanel.Mode，世界与属性及结构检查共用 Check 模式，并按入口分别显示；状态栏只显示最新操作反馈。
 * OpenCheck 选择检查或工作区；BuildIssueList 创建带输入遮罩的滚动问题列表；RefreshIssues 按草稿修订重建问题行。
 * ChooseEntry 分派文件和房间选取，RefreshTools 同步书签、选中高亮及属性说明。
 */
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban
{
    public sealed class RuntimeLayoutUI : MonoBehaviour
    {
        public LayoutHost host;
        public GameObject fileMenu,workspace,fileDialog,confirmation,keepDraft;
        public GameObject terrainCards,objectCards;
        public Text status,properties,dialogTitle,pathText,submitText,workspaceInfo,message,confirmMessage;
        public InputField fileName,fieldX,fieldY,roomName;
        public Button[] toolButtons;
        public Button terrainTab,objectTab,entryPrefab;
        public RectTransform entries,rooms;
        public Button assetTab, bindAsset;
        public InterfaceThemeScope interfaceStyle;
        public GameObject assetList;
        public RectTransform assetEntries;
        readonly AssetThumbnail thumbnails = new AssetThumbnail();
        readonly Dictionary<string,Button> cards = new Dictionary<string,Button>();
        string pickedAsset, pickedBuiltin, paletteKey;
        RoomTool paletteCategory = RoomTool.Decoration;
        bool custom, categoryOpen, terrain;
        string[] listedEntries;
        string[] roomIds;
        bool lastDialog;
        string activeRoom;
        UiLayers layers;
        public bool CoversMap => Panel.Modal || Panel.Mode.BlocksMapInput;
        internal bool PointerOverUi(Vector3 position) => layers != null && layers.PointerOverUi(position);
        LayoutPanel Panel => host.Panel;
        Color SelectedTint => interfaceStyle && interfaceStyle.theme ? interfaceStyle.theme.active : new Color32(255, 195, 104, 255);
        Color NormalTint => interfaceStyle && interfaceStyle.theme ? interfaceStyle.theme.parchment : new Color32(255, 228, 182, 255);

        void Paint(Image image, bool on, Sprite normalSprite, Sprite selectedSprite)
        {
            var sprite = on ? selectedSprite : normalSprite;
            if (sprite)
            {
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                image.color = Color.white;
            }
            else image.color = on ? SelectedTint : NormalTint;
        }

        InterfaceTheme Skin => interfaceStyle ? interfaceStyle.theme : null;

        void Start()
        {
            fileName.onValueChanged.AddListener(value => Panel.Filename=value);
            SetToolLabel(RoomTool.Box, "箱子");
            SetToolLabel(RoomTool.Target, "目标点");
            var target = toolButtons[(int)RoomTool.Target];
            var other = Instantiate(target, target.transform.parent, false);
            var rect = (RectTransform)other.transform;
            rect.anchoredPosition += Vector2.down * (rect.rect.height + 10);
            other.GetComponentInChildren<Text>().text = "其他";
            other.onClick = new Button.ButtonClickedEvent();
            other.onClick.AddListener(() => OpenCategory(RoomTool.Decoration));
            if (other.transform.Find("图标") is Transform icon) icon.gameObject.SetActive(false);
            BuildTabs();
            BuildMust();
            BuildProps();
            BuildFileMenu();
            BuildConfirmButtons();
            BuildIssueList();
            layers = new UiLayers(gameObject);
            UiLayers.SetOrder(must.gameObject, UiLayers.Persistent);
            UiLayers.SetOrder(fileMenu, UiLayers.Dropdown);
            UiLayers.SetOrder(workspace, UiLayers.Modal);
            UiLayers.SetOrder(checkOverlay, UiLayers.Modal);
            UiLayers.SetOrder(fileDialog, UiLayers.Modal + 1);
            UiLayers.SetOrder(confirmation, UiLayers.Modal + 2);
        }

        void SetToolLabel(RoomTool type, string name) => toolButtons[(int)type].GetComponentInChildren<Text>().text = name;

        bool showWorkspace;
        int tab = 1;
        Button[] tabs;
        public Button[] Bookmarks => tabs;
        static readonly string[] TabNames = { "地形", "物件", "装饰", "其他", "自定义" };

        void BuildTabs()
        {
            var prefab = Resources.Load<Button>("LayoutUI/Bookmark");
            if (!prefab || !terrainTab) return;
            var origin = (RectTransform)terrainTab.transform;
            var bar = new GameObject("页签", typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
            bar.SetParent(origin.parent, false);
            bar.anchorMin = new Vector2(0, 1);
            bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(.5f, .5f);
            bar.anchoredPosition = new Vector2(0, origin.anchoredPosition.y);
            bar.sizeDelta = new Vector2(-20, 44);
            var row = bar.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 6;
            row.childAlignment = TextAnchor.LowerLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = false;
            terrainTab.gameObject.SetActive(false);
            objectTab.gameObject.SetActive(false);
            assetTab.gameObject.SetActive(false);
            tabs = new Button[TabNames.Length];
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;
                var item = Instantiate(prefab, bar, false);
                item.name = "Bookmark";
                var label = item.GetComponentInChildren<Text>();
                label.text = TabNames[i];
                label.fontSize = 16;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                item.gameObject.AddComponent<LayoutElement>().preferredHeight = 38;
                item.onClick.AddListener(() => SelectTab(index));
                tabs[i] = item;
            }
        }

        void SelectTab(int index)
        {
            Panel.CancelTool();
            tab = index;
            custom = index == 4;
            terrain = index == 0;
            categoryOpen = false;
            pickedAsset = pickedBuiltin = null;
            if (index == 2) OpenCategory(RoomTool.Decoration);
        }

        void RefreshTabs()
        {
            if (tabs == null) return;
            for (int i = 0; i < tabs.Length; i++)
            {
                bool on = i == tab;
                Paint(tabs[i].image, on, Skin ? Skin.bookmarkSprite : null, Skin ? Skin.bookmarkSelectedSprite : null);
                tabs[i].GetComponent<LayoutElement>().preferredHeight = on ? 44 : 38;
            }
        }

        RectTransform must;
        string mustKey;
        readonly List<GameObject> mustRows = new List<GameObject>();
        static readonly string[] MustLabels = { "出生点", "入口门", "出口门", "边界封闭" };
        static readonly string[] MustRules = { "spawn.missing", "entry.missing", "exit.missing", "boundary.open" };

        void BuildMust()
        {
            var go = new GameObject("房间必填项", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            must = (RectTransform)go.transform;
            must.SetParent(transform, false);
            must.anchorMin = must.anchorMax = must.pivot = new Vector2(0, 1);
            must.anchoredPosition = new Vector2(12, -68);
            must.sizeDelta = new Vector2(210, 0);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            if (Skin && Skin.panelSmallSprite) { image.sprite = Skin.panelSmallSprite; image.type = Image.Type.Sliced; image.color = Color.white; }
            else image.color = NormalTint;
            var stack = go.GetComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(12, 12, 10, 12);
            stack.spacing = 6;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var title = new GameObject("标题", typeof(RectTransform), typeof(LayoutText), typeof(LayoutElement)).GetComponent<LayoutText>();
            title.rectTransform.SetParent(must, false);
            title.text = "房间必填项";
            title.raycastTarget = false;
            title.fontSize = 15;
            title.fontStyle = FontStyle.Bold;
            title.color = Skin ? Skin.ink : Color.black;
            title.GetComponent<LayoutElement>().preferredHeight = 24;
        }

        void RefreshMust()
        {
            if (!must) return;
            must.gameObject.SetActive(Panel.Mode.Current == Sokoban.Core.UiMode.Layout && !Panel.Modal);
            var d = Panel.Draft;
            string key = d.ActiveId + "#" + d.Revision;
            if (key == mustKey) return;
            mustKey = key;
            foreach (var row in mustRows) { row.SetActive(false); Destroy(row); }
            mustRows.Clear();
            bool[] ok = { Panel.RuleOk("spawn.missing"), Panel.RuleOk("entry.missing"), d.Model.Doors.Any(door => door.Role == "exit"), Panel.RuleOk("boundary.open") };
            for (int i = 0; i < MustLabels.Length; i++)
            {
                int index = i;
                var row = new GameObject("必填行", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
                row.transform.SetParent(must, false);
                var line = row.GetComponent<HorizontalLayoutGroup>();
                line.spacing = 6;
                line.childControlWidth = line.childControlHeight = true;
                line.childForceExpandWidth = false;
                line.childForceExpandHeight = true;
                row.GetComponent<LayoutElement>().preferredHeight = 30;
                var button = Instantiate(entryPrefab, row.transform, false);
                button.GetComponentInChildren<Text>().text = MustLabels[i];
                var size = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
                size.flexibleWidth = 1;
                size.preferredHeight = 30;
                button.onClick.AddListener(() => LocateMust(index));
                ThemeWidgets.Tag(row.transform, ok[i], ok[i] ? "已完成" : "未完成");
                mustRows.Add(row);
            }
        }

        void LocateMust(int index)
        {
            var issue = Panel.Issues.FirstOrDefault(i => i.RuleId == MustRules[index])
                ?? new Sokoban.Core.Issue(MustRules[index], Sokoban.Core.Severity.Hint, Panel.Draft.ActiveId, MustLabels[index] + "未完成");
            Panel.LocateIssue(issue);
            if (Sokoban.Core.IssueLocator.Locate(issue).Kind == Sokoban.Core.LocateKind.Room) OpenCheck(false);
        }

        void OnDestroy() => thumbnails.Dispose();

        async void PreviewCard(Button button,IReadOnlyList<Sokoban.Core.AssetDescriptor> room,string key,bool builtin=false)
        {
            var image=button.GetComponentInChildren<RawImage>(true);
            if(!image && builtin)
            {
                var icon=button.transform.Find("图标");
                if(!icon) return;
                var go=new GameObject("模型缩略图",typeof(RectTransform),typeof(RawImage));
                image=go.GetComponent<RawImage>();image.rectTransform.SetParent(icon,false);
                image.rectTransform.anchorMin=Vector2.zero;image.rectTransform.anchorMax=Vector2.one;
                image.rectTransform.offsetMin=image.rectTransform.offsetMax=Vector2.zero;image.raycastTarget=false;image.enabled=false;
            }
            if(!image) return;
            var texture=await thumbnails.Get(room,key,builtin);
            if(image && texture) { image.texture=texture;image.enabled=true; }
        }
        void RefreshAssets()
        {
            var draft = Panel.Draft;
            var pack = draft.PackParts().ToArray();
            string category = CategoryId(paletteCategory);
            var entries = draft.Model.Assets.Where(asset => !pack.Any(item => item.asset.Id == asset.Id));
            var assets = custom ? entries.Select(asset => asset.Id).ToArray()
                : categoryOpen ? pack.Where(item => item.category == category).OrderByDescending(item => item.theme == draft.Theme)
                    .ThenBy(item => item.asset.Name, System.StringComparer.Ordinal).Select(item => item.asset.Id)
                    .Concat(entries.Where(asset => asset.Kind == Sokoban.Core.AssetKinds.Model).Select(asset => asset.Id))
                    .Distinct().ToArray() : System.Array.Empty<string>();
            var definitions = custom ? draft.Model.Templates.Values.ToArray() : null;
            string theme = draft.Theme;
            string key = draft.ActiveId + theme + custom + categoryOpen + paletteCategory + (definitions == null ? "" : string.Join(",", definitions.Select(t => t.Id + t.Name))) + string.Join(",", assets);
            if (paletteKey != key)
            {
                paletteKey = key;
                foreach (var button in cards.Values) { button.gameObject.SetActive(false); Destroy(button.gameObject); }
                cards.Clear();
                if (pickedAsset != null && draft.AssetDefinition(pickedAsset) == null) pickedAsset = null;
                if (categoryOpen)
                {
                    AddPaletteCard("back", "‹ 返回 · " + AppearanceLibrary.TypeName(CategoryId(paletteCategory)), null, () => { categoryOpen = false; pickedAsset = pickedBuiltin = null; Panel.CancelTool(); });
                    AddPaletteCard("import", "＋ 新增" + AppearanceLibrary.TypeName(CategoryId(paletteCategory)) + "模型", null,
                        () => { pickedAsset = pickedBuiltin = null; Tool(paletteCategory); Panel.ChooseFile("导入模型"); }, false, true);
                    foreach (var entry in AppearanceLibrary.Entries(CategoryId(paletteCategory), theme))
                        AddPaletteCard(entry.Id, entry.Name, entry.Prefab(0), () =>
                        {
                            Tool(paletteCategory);
                            pickedAsset = null;
                            pickedBuiltin = entry.Id;
                            Panel.Run(() => draft.ChooseAppearance(entry.Id, CategoryId(paletteCategory)), "已选 " + entry.Name + "，在地图放置");
                        }, true);
                }
                else if (custom)
                    AddPaletteCard("import", "＋ 新增自定义模型", null, () => { Panel.CancelTool(); Panel.ChooseFile("导入模型"); }, false, true);
                if (definitions != null)
                    foreach (var entry in definitions)
                    {
                        string id = entry.Id;
                        string model = entry.Appearance?.Id;
                        AddPaletteCard(id, entry.Name + " · 对象模板", model, () =>
                        {
                            Panel.CancelTool();
                            pickedAsset = pickedBuiltin = null;
                            string issue = thumbnails.ModelIssue(draft.Model.Assets, model);
                            if (issue != null) Panel.Status = issue;
                            else Panel.Run(() =>
                            {
                                draft.Tool = RoomTool.Decoration;
                                draft.SelectDefinition(id);
                            }, "左键放置功能物件，短按右键取消");
                        });
                    }
                foreach (string id in assets)
                {
                    var asset = draft.AssetDefinition(id);
                    string kind = asset.Kind;
                    string label = kind == "model" ? "" : kind == "animation" ? " · 动作" : kind == "texture" ? " · 贴图" : " · 界面图";
                    AddPaletteCard(id, asset.Name + label, id, () =>
                    {
                        Panel.CancelTool();
                        pickedAsset = id;
                        pickedBuiltin = null;
                        if (kind != "model") return;
                        string issue = thumbnails.ModelIssue(draft.Model.Assets, id, !pack.Any(item => item.asset.Id == id));
                        if (issue != null) Panel.Status = issue;
                        else Panel.Run(() =>
                        {
                            draft.Tool = custom ? RoomTool.Decoration : paletteCategory;
                            draft.ChooseAppearance(id, custom ? Sokoban.Core.ObjectCategories.Custom : CategoryId(paletteCategory));
                        }, "左键放置，短按右键取消");
                    });
                }
            }
            bindAsset.gameObject.SetActive((custom || categoryOpen) && (pickedAsset != null || pickedBuiltin != null));
            if (pickedBuiltin != null)
                bindAsset.interactable = draft.CanReplaceAppearance(pickedBuiltin);
            else if (pickedAsset != null)
                bindAsset.interactable = draft.CanBindAsset(pickedAsset, categoryOpen ? CategoryId(paletteCategory) : null) &&
                    thumbnails.ModelIssue(draft.Model.Assets, pickedAsset, !pack.Any(item => item.asset.Id == pickedAsset)) == null;
            foreach (var card in cards)
            {
                if (card.Key == "import") continue;
                Paint(card.Value.image, card.Key == (draft.BrushTemplate ?? draft.BrushAppearance?.Id ?? pickedBuiltin ?? pickedAsset),
                    Skin ? Skin.cardSprite : null, Skin ? Skin.cardSelectedSprite : null);
            }
        }

        void AddPaletteCard(string id, string title, string model, UnityEngine.Events.UnityAction action, bool builtin = false, bool dashed = false)
        {
            var button = Instantiate(entryPrefab, assetEntries, false);
            cards.Add(id, button);
            var label = button.GetComponentInChildren<Text>();
            label.text = title;
            button.onClick.AddListener(action);
            if (dashed)
            {
                if (Skin && Skin.cardDashedSprite) { button.image.sprite = Skin.cardDashedSprite; button.image.type = Image.Type.Sliced; button.image.color = Color.white; }
                label.color = new Color32(139, 106, 72, 255);
            }
            if (model == null) return;
            bool editable = !builtin && id == model && Panel.Draft.Model.Assets.Any(a => a.Id == id && a.Kind == Sokoban.Core.AssetKinds.Model);
            var content = new GameObject("卡片内容", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var contentRect = (RectTransform)content.transform;
            contentRect.SetParent(button.transform, false);
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = new Vector2(editable ? -116 : 0, 0);
            var row = content.GetComponent<HorizontalLayoutGroup>();
            var border = button.image.sprite ? button.image.sprite.border / button.image.pixelsPerUnit : Vector4.zero;
            row.padding = new RectOffset(Mathf.CeilToInt(border.x) + 4, Mathf.CeilToInt(border.z) + 4, 0, 0);
            row.spacing = 6;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            var frame = ThemeWidgets.Thumb(contentRect, 44f);
            label.rectTransform.SetParent(contentRect, false);
            var labelSize = label.gameObject.AddComponent<LayoutElement>();
            labelSize.minWidth = labelSize.preferredWidth = 0;
            labelSize.flexibleWidth = 1;
            var thumb = new GameObject("缩略图", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            thumb.rectTransform.SetParent(frame, false);
            thumb.rectTransform.anchorMin = Vector2.zero;
            thumb.rectTransform.anchorMax = Vector2.one;
            thumb.rectTransform.offsetMin = new Vector2(4, 4);
            thumb.rectTransform.offsetMax = new Vector2(-4, -4);
            thumb.raycastTarget = false;
            thumb.enabled = false;
            PreviewCard(button, Panel.Draft.Model.Assets, model, builtin);
            if (editable)
            {
                AssetAction(button.transform, "改名", -64, () => BeginRename(button, id));
                AssetAction(button.transform, "删除", -8, () => Panel.Run(() => RemoveAsset(id), "已删除自建模型"));
            }
        }

        void AssetAction(Transform card, string text, float right, UnityEngine.Events.UnityAction action)
        {
            var item = Instantiate(entryPrefab, card, false);
            item.name = text;
            item.GetComponentInChildren<Text>().text = text;
            item.onClick.AddListener(action);
            var rect = (RectTransform)item.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1, .5f);
            rect.anchoredPosition = new Vector2(right, 0);
            rect.sizeDelta = new Vector2(52, 30);
            if (text == "删除" && Skin && Skin.buttonDangerSprite) { item.image.sprite = Skin.buttonDangerSprite; item.image.type = Image.Type.Sliced; item.image.color = Color.white; }
        }

        void BeginRename(Button card, string id)
        {
            var asset = Panel.Draft.Model.Assets.FirstOrDefault(a => a.Id == id);
            if (asset == null) return;
            var field = Instantiate(roomName, card.transform, false);
            field.name = "改名输入";
            field.onEndEdit = new InputField.EndEditEvent();
            field.onValueChanged = new InputField.OnChangeEvent();
            var rect = (RectTransform)field.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6, 4);
            rect.offsetMax = new Vector2(-6, -4);
            field.SetTextWithoutNotify(asset.Name);
            field.Select();
            field.ActivateInputField();
            bool done = false;
            field.onEndEdit.AddListener(value =>
            {
                if (done) return;
                done = true;
                Destroy(field.gameObject);
                Panel.Run(() => RenameAsset(id, value), "已改名");
            });
        }

        void RenameAsset(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new System.InvalidOperationException("名称不能为空");
            var draft = Panel.Draft;
            var model = draft.Model;
            int index = model.Assets.FindIndex(a => a.Id == id);
            if (index < 0) throw new System.InvalidOperationException("当前房间没有所选素材");
            var old = model.Assets[index];
            if (old.Name == name.Trim()) return;
            var next = new Sokoban.Core.AssetDescriptor(old.Id, old.Kind, name.Trim(), old.Sha256, old.Extension);
            draft.Commit(new ModelEdit(
                () => model.Assets[model.Assets.FindIndex(a => a.Id == id)] = next,
                () => model.Assets[model.Assets.FindIndex(a => a.Id == id)] = old));
            paletteKey = null;
        }

        static bool AssetInUse(Sokoban.Core.RoomDraftModel model, string id)
        {
            var room = model.Room;
            if (room.Objects.Any(o => o.Appearance?.Id == id)) return true;
            if (model.Decorations.Any(d => d.Appearance?.Id == id)) return true;
            if (model.Templates.Values.Any(t => t.Appearance?.Id == id)) return true;
            for (int x = 0; x < room.Width; x++)
                for (int y = 0; y < room.Height; y++)
                    if (room.AppearanceAt(new Sokoban.Core.Cell(x, y))?.Id == id) return true;
            return false;
        }

        void RemoveAsset(string id)
        {
            var draft = Panel.Draft;
            var model = draft.Model;
            int index = model.Assets.FindIndex(a => a.Id == id);
            if (index < 0) throw new System.InvalidOperationException("当前房间没有所选素材");
            if (AssetInUse(model, id)) throw new System.InvalidOperationException("该模型仍被地图上的内容使用，请先替换或删除");
            var old = model.Assets[index];
            draft.Commit(new ModelEdit(
                () => model.Assets.RemoveAll(a => a.Id == id),
                () => model.Assets.Insert(System.Math.Min(index, model.Assets.Count), old)));
            if (pickedAsset == id) pickedAsset = null;
            paletteKey = null;
        }

        static string CategoryId(RoomTool type) => type switch
        {
            RoomTool.Box => Sokoban.Core.ObjectCategories.Box,
            RoomTool.Target => Sokoban.Core.ObjectCategories.Target,
            RoomTool.Wall => Sokoban.Core.TerrainCategories.Wall,
            RoomTool.Floor => Sokoban.Core.TerrainCategories.Floor,
            _ => AppearanceLibrary.Decoration
        };

        void OpenCategory(RoomTool type)
        {
            custom = false;
            categoryOpen = true;
            paletteCategory = type;
            pickedAsset = pickedBuiltin = null;
            Tool(type);
        }
        public void Act(string action)
        {
            var d=Panel.Draft;
            switch(action)
            {
                case "文件":
                    fileMenu.SetActive(!fileMenu.activeSelf);
                    if (fileMenu.activeSelf) UiLayers.SetOrder(fileMenu, UiLayers.Dropdown);
                    return;
                case "保存": case "打开": case "导入": case "导出": case "发布": case "导出单房": case "导入素材包": case "导入模型": case "导入动作": case "导入贴图": case "导入界面图": Panel.ChooseFile(action);break;
                case "新建": Panel.NewRoom(true);break;
                case "默认布局": Panel.NewDefaultRoom();break;
                case "撤销": Panel.Run(d.Undo,"已撤销");break;
                case "重做": Panel.Run(d.Redo,"已重做");break;
                case "网格": host.GridVisible=!host.GridVisible;break;
                case "试玩": fileMenu.SetActive(false); host.StartTrial(); return;
                case "离开": host.Leave();break;
                case "工作区": OpenCheck(true);break;
                case "返回": Panel.CloseCheck();break;
                case "选物件": tab = 1; custom = categoryOpen = false; terrain = false; pickedAsset = pickedBuiltin = null; break;
                case "选地形": tab = 0; custom = categoryOpen = false; terrain = true; pickedAsset = pickedBuiltin = null; break;
                case "选素材":
                    tab = 4; custom = true; categoryOpen = false; pickedAsset = pickedBuiltin = null; break;
                case "绑定素材":
                    Panel.Run(() =>
                    {
                        if (pickedBuiltin != null) d.ReplaceAppearance(pickedBuiltin);
                        else d.BindAsset(pickedAsset, categoryOpen ? CategoryId(paletteCategory) : null);
                        Panel.CancelTool();
                    }, "已更新外观绑定");
                    break;
                case "切换房间视图": host.ToggleRoomView();if(Panel.Mode.Current==Sokoban.Core.UiMode.Check)Panel.CloseCheck();break;
                case "选择": Tool(RoomTool.Select);break;
                case "纸箱": OpenCategory(RoomTool.Box); break;
                case "目标": OpenCategory(RoomTool.Target); break;
                case "墙壁": OpenCategory(RoomTool.Wall); break;
                case "地板": OpenCategory(RoomTool.Floor); break;
                case "擦除": Tool(RoomTool.Erase);break;
                case "移动": Tool(RoomTool.Move);break;
                case "建世界": Panel.Run(d.BeginWorld,"世界已创建，请放置房间");break;
                case "加入房间": Panel.ChooseFile(action);break;
                case "整房预览": if(d.World!=null){Panel.StartRoomPlacement();if(Panel.Mode.Current==Sokoban.Core.UiMode.Check)Panel.CloseCheck();}break;
                case "按坐标放置": ApplyPoint(true);break;
                case "移动对象": ApplyPoint(false);break;
                case "移出": Panel.Run(d.RemoveRoom,"房间已移回目录");break;
                case "名称": Panel.Run(()=>d.Rename(roomName.text),"已修改名称");break;
                case "关闭检查": Panel.CloseCheck(); break;
                case "检查": OpenCheck(false);break;
                case "上一级": Panel.ParentDirectory();break;
                case "提交文件": Panel.SubmitFile();break;
                case "取消": Panel.CancelDialog();break;
                case "保存继续": Panel.ContinueAfterSave();break;
                case "放弃": Panel.DiscardAndContinue();break;
                case "保留": Panel.KeepAndExit();break;
                case "保留退出": if(Panel.Flush()) UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("MainMenu");break;
            }
            fileMenu.SetActive(false);
        }

        void Tool(RoomTool tool)
        {
            Panel.CancelTool();
            Panel.Draft.Tool=tool;
            switch (tool)
            {
                case RoomTool.Move: Panel.Status="点击空地移动选中对象，短按右键取消"; break;
                case RoomTool.Box: case RoomTool.Target: Panel.Status="左键放置，短按右键取消"; break;
                case RoomTool.Floor: case RoomTool.Wall: Panel.Status="按住左键连续铺设，松开提交"; break;
                case RoomTool.Erase: Panel.Status="点击格子擦除，按住左键连续擦除"; break;
            }
        }
        void ApplyPoint(bool wholeRoom)
        {
            if(!int.TryParse(fieldX.text,out int x)||!int.TryParse(fieldY.text,out int y)){Panel.Status="请输入整数坐标";return;}
            var point=new Sokoban.Core.Cell(x,y);var d=Panel.Draft;
            if(wholeRoom)Panel.Run(()=>d.PlaceRoom(point,d.Placement!=null),"已更新房间原点");
            else Panel.Run(()=>d.Move(point-d.Origin),"已移动对象");
        }

        int listedRevision = -1;
        void RefreshEntries(RectTransform container,string[] items,bool directory)
        {
            for(int i=container.childCount-1;i>=0;i--){var old=container.GetChild(i).gameObject;old.SetActive(false);Destroy(old);}
            foreach(string item in items)
            {
                var button=Instantiate(entryPrefab,container,false);
                string label=directory?LayoutPanel.EntryIcon(item)+"  "+Path.GetFileName(item):Panel.Draft.Catalog[item].Meta.Name+"   "+item;
                var rowLabel=button.GetComponentInChildren<Text>();
                rowLabel.text=label;
                if(directory)
                {
                    rowLabel.alignment=TextAnchor.MiddleLeft;
                    var note=CardText(button.transform,12,true);
                    note.text=Panel.EntryNote(item);
                    note.alignment=TextAnchor.MiddleRight;
                    note.rectTransform.anchorMin=Vector2.zero;
                    note.rectTransform.anchorMax=Vector2.one;
                    note.rectTransform.offsetMin=new Vector2(12,0);
                    note.rectTransform.offsetMax=new Vector2(-12,0);
                }
                button.onClick.AddListener(()=>ChooseEntry(item,directory));
            }
            if (!directory)
            {
                if (Panel.Draft.BoxSelected)
                    for (int i = 0; i < RoomDraft.BuiltinBoxes.Length; i++)
                    {
                        string model = RoomDraft.BuiltinBoxes[i];
                        var bind = Instantiate(entryPrefab,container,false);
                        bind.GetComponentInChildren<Text>().text = "绑定内置模型 · " + RoomDraft.BuiltinBoxNames[i];
                        bind.onClick.AddListener(() => Panel.Run(() => Panel.Draft.BindBuiltinBox(model),"已绑定箱体模型"));
                    }
                var preset = Instantiate(entryPrefab,container,false);
                preset.GetComponentInChildren<Text>().text = Panel.RecoveryLabel;
                preset.onClick.AddListener(Panel.CycleRecovery);
                var theme = Instantiate(entryPrefab, container, false);
                theme.GetComponentInChildren<Text>().text = Panel.ThemeLabel;
                theme.onClick.AddListener(Panel.CycleTheme);
            }
            if (!directory && Panel.Draft.SelectedCell.HasValue)
                for (int i = 0; i < LayoutPanel.MarkerLabels.Length; i++)
                {
                    int index = i;
                    var button = Instantiate(entryPrefab, container, false);
                    button.GetComponentInChildren<Text>().text = LayoutPanel.MarkerLabels[index];
                    button.onClick.AddListener(() => { Panel.ApplyMarker(index); if (Panel.Mode.Current == Sokoban.Core.UiMode.Check) Panel.CloseCheck(); });
                }
            if (!directory && Panel.Draft.World != null)
                foreach (var link in Panel.Draft.World.Connections)
                {
                    var button = Instantiate(entryPrefab, container, false);
                    button.GetComponentInChildren<Text>().text = Panel.ConnectionAction(link);
                    button.onClick.AddListener(() => Panel.ToggleConnection(link));
                }
            var scroll=container.GetComponentInParent<ScrollRect>();
            if(scroll)scroll.verticalNormalizedPosition=1;
        }
        static readonly string[] MenuItems = { "新建空白房间", "打开", "导入", "导出", "导出单房", "发布世界", "导入素材包", "导入模型", "导入动作", "导入贴图", "导入界面图", "世界与属性", "检查结构" };
        static readonly string[] MenuActs = { "新建", "打开", "导入", "导出", "导出单房", "发布", "导入素材包", "导入模型", "导入动作", "导入贴图", "导入界面图", "工作区", "检查" };

        void BuildFileMenu()
        {
            var menu = (RectTransform)fileMenu.transform;
            for (int i = menu.childCount - 1; i >= 0; i--) Destroy(menu.GetChild(i).gameObject);
            menu.anchorMin = menu.anchorMax = menu.pivot = new Vector2(0, 1);
            menu.anchoredPosition = new Vector2(14, -60);
            menu.sizeDelta = new Vector2(340, 0);
            var back = fileMenu.GetComponent<Image>();
            if (back && Skin && Skin.panelSprite) { back.sprite = Skin.panelSprite; back.type = Image.Type.Sliced; back.color = Color.white; }
            var stack = fileMenu.GetComponent<VerticalLayoutGroup>() ?? fileMenu.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(14, 14, 12, 16);
            stack.spacing = 6;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            var fit = fileMenu.GetComponent<ContentSizeFitter>() ?? fileMenu.AddComponent<ContentSizeFitter>();
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var grid = new GameObject("两列", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            grid.transform.SetParent(menu, false);
            var cells = grid.GetComponent<GridLayoutGroup>();
            cells.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            cells.constraintCount = 2;
            cells.cellSize = new Vector2(150, 34);
            cells.spacing = new Vector2(6, 6);
            int rows = (MenuItems.Length + 1) / 2;
            grid.GetComponent<LayoutElement>().preferredHeight = rows * 34 + (rows - 1) * 6;
            for (int i = 0; i < MenuItems.Length; i++)
            {
                string act = MenuActs[i];
                CardButton(grid.transform, MenuItems[i], false, () => Act(act));
            }
            CardButton(menu, "保留草稿退出", true, () => Act("保留退出"));
            CardButton(menu, "返回主菜单", true, () => Act("离开"));
        }

        void BuildConfirmButtons()
        {
            if (!confirmation || confirmation.transform.childCount == 0) return;
            var panel = (RectTransform)confirmation.transform.GetChild(0);
            panel.sizeDelta = new Vector2(440, 288);
            void Place(string name, string text, float x, float y, float width)
            {
                var item = panel.Find(name) as RectTransform;
                if (!item) return;
                item.anchorMin = item.anchorMax = item.pivot = new Vector2(.5f, 0);
                item.anchoredPosition = new Vector2(x, y);
                item.sizeDelta = new Vector2(width, 40);
                if (text != null) { var label = item.GetComponentInChildren<Text>(); if (label) label.text = text; }
            }
            Place("保存并继续", "保存", -120, 92, 110);
            Place("放弃修改并继续", "放弃", 0, 92, 110);
            Place("取消", null, 120, 92, 110);
            Place("保留草稿退出", null, 0, 40, 240);
        }

        GameObject checkOverlay;
        RectTransform issueList;
        string issueKey;
        readonly List<GameObject> issueRows = new List<GameObject>();

        void OpenCheck(bool workspaceRequested)
        {
            showWorkspace = workspaceRequested;
            fileMenu.SetActive(false);
            Panel.CheckCurrentRoom();
        }

        void BuildIssueList()
        {
            checkOverlay = new GameObject("结构检查", typeof(RectTransform), typeof(Image));
            var overlay = (RectTransform)checkOverlay.transform;
            overlay.SetParent(workspace.transform.parent, false);
            overlay.anchorMin = Vector2.zero;
            overlay.anchorMax = Vector2.one;
            overlay.offsetMin = overlay.offsetMax = Vector2.zero;
            checkOverlay.GetComponent<Image>().color = new Color(0, 0, 0, .18f);
            checkOverlay.SetActive(false);

            var panel = new GameObject("问题面板", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            var panelRect = (RectTransform)panel.transform;
            panelRect.SetParent(overlay, false);
            panelRect.anchorMin = new Vector2(1, 0);
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = new Vector2(-392, 12);
            panelRect.offsetMax = new Vector2(-12, -68);
            var image = panel.GetComponent<Image>();
            if (Skin && Skin.panelSmallSprite) { image.sprite = Skin.panelSmallSprite; image.type = Image.Type.Sliced; image.color = Color.white; }
            else image.color = NormalTint;
            var panelStack = panel.GetComponent<VerticalLayoutGroup>();
            panelStack.padding = new RectOffset(12, 12, 10, 12);
            panelStack.spacing = 6;
            panelStack.childControlWidth = panelStack.childControlHeight = true;
            panelStack.childForceExpandWidth = true;
            panelStack.childForceExpandHeight = false;
            CardButton(panelRect, "返回布局", false, Panel.CloseCheck);

            var viewport = new GameObject("问题滚动区", typeof(RectTransform), typeof(RectMask2D), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
            var viewportRect = (RectTransform)viewport.transform;
            viewportRect.SetParent(panelRect, false);
            viewport.GetComponent<Image>().color = Color.clear;
            viewport.GetComponent<LayoutElement>().flexibleHeight = 1;
            var issueScroll = viewport.GetComponent<ScrollRect>();
            issueScroll.viewport = viewportRect;
            issueScroll.horizontal = false;
            issueScroll.movementType = ScrollRect.MovementType.Clamped;
            issueScroll.scrollSensitivity = 28;

            var list = new GameObject("问题列表", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            issueList = (RectTransform)list.transform;
            issueList.SetParent(viewportRect, false);
            issueList.anchorMin = new Vector2(0, 1);
            issueList.anchorMax = Vector2.one;
            issueList.pivot = new Vector2(.5f, 1);
            issueList.offsetMin = issueList.offsetMax = Vector2.zero;
            issueScroll.content = issueList;
            var stack = list.GetComponent<VerticalLayoutGroup>();
            stack.spacing = 6;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            list.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        void RefreshIssues()
        {
            if (!issueList) return;
            var d = Panel.Draft;
            string key = d.ActiveId + "#" + d.Revision;
            if (key == issueKey) return;
            issueKey = key;
            foreach (var row in issueRows) { row.SetActive(false); Destroy(row); }
            issueRows.Clear();
            var title = new GameObject("标题", typeof(RectTransform), typeof(LayoutElement));
            title.transform.SetParent(issueList, false);
            title.GetComponent<LayoutElement>().preferredHeight = 24;
            var head = CardText(title.transform, 15, true);
            head.rectTransform.anchorMin = Vector2.zero;
            head.rectTransform.anchorMax = Vector2.one;
            head.rectTransform.offsetMin = head.rectTransform.offsetMax = Vector2.zero;
            head.text = Panel.Issues.Count == 0 ? "问题列表 · 没有问题" : "问题列表 · " + Panel.Issues.Count + " 项";
            issueRows.Add(title);
            issueList.anchoredPosition = Vector2.zero;
            foreach (var issue in Panel.Issues)
            {
                var captured = issue;
                var button = CardButton(issueList, "[" + issue.RuleId + "] " + issue.Message, false, () => Panel.LocateIssue(captured));
                issueRows.Add(button.gameObject);
                if (issue.RuleId != "spawn.missing") continue;
                var quick = CardButton(issueList, "一键设置出生点（入口内侧）", false, () => { Panel.LocateIssue(captured); Panel.ApplyMarker(0); });
                issueRows.Add(quick.gameObject);
            }
        }

        Text dialogNote;
        GameObject dialogWarn;
        Button browseButton;
        string dialogWarnText;

        void RefreshDialogExtras()
        {
            var panel=fileDialog.transform.childCount>0?(RectTransform)fileDialog.transform.GetChild(0):null;
            if(!panel)return;
            panel.sizeDelta=new Vector2(580,panel.sizeDelta.y);
            if(!dialogNote)
            {
                dialogNote=CardText(panel,13,true);
                dialogNote.name="说明";
                dialogNote.rectTransform.anchorMin=dialogNote.rectTransform.anchorMax=dialogNote.rectTransform.pivot=new Vector2(.5f,1);
                dialogNote.rectTransform.anchoredPosition=new Vector2(0,-48);
                dialogNote.rectTransform.sizeDelta=new Vector2(520,22);
            }
            dialogNote.text=Panel.DialogNote;
            if(!browseButton)
            {
                browseButton=Instantiate(entryPrefab,panel,false);
                browseButton.name="浏览";
                browseButton.GetComponentInChildren<Text>().text="浏览";
                var rect=(RectTransform)browseButton.transform;
                rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(1,1);
                rect.anchoredPosition=new Vector2(-24,-72);
                rect.sizeDelta=new Vector2(92,32);
                browseButton.onClick.AddListener(Panel.RefreshDirectory);
            }
            string warn=Panel.DialogWarning;
            if(warn==dialogWarnText&&(warn==null||dialogWarn))return;
            dialogWarnText=warn;
            if(dialogWarn){dialogWarn.SetActive(false);Destroy(dialogWarn);dialogWarn=null;}
            if(warn==null)return;
            var line=ThemeWidgets.WarnLine(panel,warn);
            dialogWarn=line.gameObject;
            var lineRect=(RectTransform)dialogWarn.transform;
            lineRect.anchorMin=lineRect.anchorMax=lineRect.pivot=new Vector2(.5f,0);
            lineRect.anchoredPosition=new Vector2(0,94);
            lineRect.sizeDelta=new Vector2(532,36);
        }

        void ChooseEntry(string item,bool directory)
        {
            if(directory)Panel.PickEntry(item);
            else {Panel.CancelTool();Panel.Draft.UseRoom(item);}
        }

        RectTransform propCard;
        Image propThumb;
        Text propTitle, propId;
        string propKey;

        Text CardText(Transform parent, int size, bool bold)
        {
            var text = new GameObject("文字", typeof(RectTransform), typeof(LayoutText)).GetComponent<LayoutText>();
            text.rectTransform.SetParent(parent, false);
            text.fontSize = size;
            text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Skin ? Skin.ink : Color.black;
            text.raycastTarget = false;
            return text;
        }

        Button CardButton(Transform parent, string label, bool danger, UnityEngine.Events.UnityAction action)
        {
            var button = Instantiate(entryPrefab, parent, false);
            button.GetComponentInChildren<Text>().text = label;
            button.onClick.AddListener(action);
            var size = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
            size.flexibleWidth = 1;
            size.preferredHeight = 32;
            if (danger && Skin && Skin.buttonDangerSprite) { button.image.sprite = Skin.buttonDangerSprite; button.image.type = Image.Type.Sliced; button.image.color = Color.white; }
            return button;
        }

        void BuildProps()
        {
            var source = properties.rectTransform;
            propCard = ThemeWidgets.SmallPanel(source.parent).transform as RectTransform;
            propCard.name = "属性卡";
            propCard.anchorMin = source.anchorMin;
            propCard.anchorMax = source.anchorMax;
            propCard.pivot = source.pivot;
            propCard.offsetMin = source.offsetMin;
            propCard.offsetMax = source.offsetMax;
            propCard.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            var stack = propCard.gameObject.GetComponent<VerticalLayoutGroup>() ?? propCard.gameObject.AddComponent<VerticalLayoutGroup>();
            stack.padding = new RectOffset(12, 12, 10, 12);
            stack.spacing = 10;
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = true;
            stack.childForceExpandHeight = false;
            var head = new GameObject("头部", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            head.transform.SetParent(propCard, false);
            var line = head.GetComponent<HorizontalLayoutGroup>();
            line.spacing = 12;
            line.childAlignment = TextAnchor.MiddleLeft;
            line.childControlWidth = line.childControlHeight = true;
            line.childForceExpandWidth = false;
            line.childForceExpandHeight = false;
            head.GetComponent<LayoutElement>().preferredHeight = 56;
            var frame = ThemeWidgets.Thumb(head.transform, 56f).transform as RectTransform;
            propThumb = frame.GetComponent<Image>();
            var frameSize = frame.gameObject.GetComponent<LayoutElement>() ?? frame.gameObject.AddComponent<LayoutElement>();
            frameSize.preferredWidth = frameSize.preferredHeight = 56;
            var column = new GameObject("文字列", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            column.transform.SetParent(head.transform, false);
            var texts = column.GetComponent<VerticalLayoutGroup>();
            texts.spacing = 4;
            texts.childControlWidth = texts.childControlHeight = true;
            texts.childForceExpandWidth = true;
            texts.childForceExpandHeight = false;
            column.GetComponent<LayoutElement>().flexibleWidth = 1;
            propTitle = CardText(column.transform, 16, true);
            propId = CardText(column.transform, 12, true);
            var buttons = new GameObject("按钮行", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            buttons.transform.SetParent(propCard, false);
            var row = buttons.GetComponent<HorizontalLayoutGroup>();
            row.spacing = 8;
            row.childControlWidth = row.childControlHeight = true;
            row.childForceExpandWidth = true;
            row.childForceExpandHeight = false;
            buttons.GetComponent<LayoutElement>().preferredHeight = 32;
            CardButton(buttons.transform, "移动", false, () => { Panel.CancelTool(); Panel.Draft.Tool = RoomTool.Move; Panel.Status = "点击空地移动选中对象，短按右键取消"; });
            CardButton(buttons.transform, "删除", true, () => { var id = Panel.Draft.Selected; if (id != null) Panel.Run(() => Panel.Draft.RemoveObject(id), "已删除"); });
            propCard.gameObject.SetActive(false);
        }

        void RefreshProps(Sokoban.Core.PlacedObject obj, bool allowed)
        {
            if (!propCard) return;
            bool show = allowed && obj != null;
            propCard.gameObject.SetActive(show);
            properties.gameObject.SetActive(!show);
            if (!show) return;
            var draft = Panel.Draft;
            string key = draft.Selected + "#" + draft.Revision;
            if (key == propKey) return;
            propKey = key;
            bool box = obj.CategoryId == Sokoban.Core.ObjectCategories.Box, target = obj.CategoryId == Sokoban.Core.ObjectCategories.Target;
            propTitle.text = AppearanceLibrary.TypeName(obj.CategoryId);
            string shortId = obj.Id.Length > 8 ? obj.Id.Substring(0, 8) + "…" : obj.Id;
            propId.horizontalOverflow = HorizontalWrapMode.Wrap;
            propId.text = "编号 #" + shortId + " · 位置 " + obj.Cell;
            propThumb.color = box ? new Color32(183, 126, 83, 255) : target ? new Color32(57, 151, 134, 255) : new Color32(154, 167, 181, 255);
        }

        void RefreshTools()
        {
            var draft=Panel.Draft;
            objectCards.SetActive(tab == 1 && !categoryOpen);
            terrainCards.SetActive(tab == 0 && !categoryOpen);
            assetList.SetActive(custom || categoryOpen);
            RefreshAssets();
            Color selected = SelectedTint, normal = NormalTint;
            RefreshTabs();
            for(int i=0;i<toolButtons.Length;i++)
                if(toolButtons[i])Paint(toolButtons[i].image, (int)draft.Tool==i, Skin ? Skin.buttonSprite : null, Skin ? Skin.buttonSelectedSprite : null);
            var obj=draft.Find(draft.Selected);
            RefreshProps(obj, !custom && !categoryOpen && draft.Tool != RoomTool.Box && draft.Tool != RoomTool.Target);
            if (custom && draft.BrushTemplate != null)
            {
                properties.text = ObjectTemplates.Find(draft.Model, draft.BrushTemplate).Name + "\n每次放置都会创建独立行为。";
                return;
            }
            if ((custom || categoryOpen) && pickedAsset != null)
            {
                var asset = draft.AssetDefinition(pickedAsset);
                properties.text = asset.Name + "  " + thumbnails.SizeReport(pickedAsset) + "\n" + draft.AssetBindingTarget(pickedAsset);
                return;
            }
            if (categoryOpen && pickedBuiltin != null)
            {
                var look = AppearanceLibrary.Find(pickedBuiltin);
                properties.text = look.Name + "\n" + AppearanceLibrary.TypeName(look.CategoryId);
                return;
            }
            properties.text=draft.Tool==RoomTool.Box?"箱子\n在空地上放置一个箱子。"
                :draft.Tool==RoomTool.Target?"目标点\n标记箱子需要到达的格子。"
                :obj!=null?AppearanceLibrary.TypeName(obj.CategoryId)+"\n"+draft.Selected+"\n当前格 "+obj.Cell
                :draft.Model.Meta.Name+"\n"+draft.Room.Width+"×"+draft.Room.Height;
        }

        void LateUpdate()
        {
            if(!host || Panel==null)return;
            bool checking = Panel.Mode.Current == Sokoban.Core.UiMode.Check;
            if (checking && !Panel.Modal && Input.GetKeyDown(KeyCode.Escape))
            {
                Panel.CloseCheck();
                checking = Panel.Mode.Current == Sokoban.Core.UiMode.Check;
            }
            bool dialog=Panel.Operation!=null && !Panel.Confirming;
            fileDialog.SetActive(dialog);
            confirmation.SetActive(Panel.Confirming);
            keepDraft.SetActive(Panel.CanKeepDraft);
            workspace.SetActive(checking && showWorkspace && !Panel.Modal);
            checkOverlay.SetActive(checking && !showWorkspace && !Panel.Modal);
            if (workspace.activeInHierarchy) UiLayers.SetOrder(workspace, UiLayers.Modal);
            if (checkOverlay.activeInHierarchy) UiLayers.SetOrder(checkOverlay, UiLayers.Modal);
            if (fileDialog.activeInHierarchy) UiLayers.SetOrder(fileDialog, UiLayers.Modal + 1);
            if (confirmation.activeInHierarchy) UiLayers.SetOrder(confirmation, UiLayers.Modal + 2);
            if(dialog)
            {
                dialogTitle.text=Panel.DialogTitle;
                RefreshDialogExtras();
                pathText.text=Panel.DirectoryPath;
                if(!lastDialog || !ReferenceEquals(listedEntries,Panel.Entries))
                {
                    listedEntries=Panel.Entries;
                    RefreshEntries(entries,listedEntries,true);
                }
                if(fileName.text!=Panel.Filename)fileName.SetTextWithoutNotify(Panel.Filename??"");
                submitText.text=Panel.Overwrite?"确认覆盖":Panel.Operation;
                message.text=string.IsNullOrEmpty(Panel.Status) ? "可输入完整文件路径" : Panel.Status;
            }
            lastDialog=dialog;
            if(Panel.Confirming)confirmMessage.text=Panel.Status;
            if(workspace.activeSelf)
            {
                var d=Panel.Draft;
                var ids=d.Catalog.Keys.ToArray();
                if(listedRevision != d.Revision || roomIds==null || !roomIds.SequenceEqual(ids) || activeRoom!=d.ActiveId)
                {
                    RefreshEntries(rooms,ids,false);
                    listedRevision=d.Revision;roomIds=ids;activeRoom=d.ActiveId;
                    roomName.SetTextWithoutNotify(d.Model.Meta.Name);
                }
                workspaceInfo.text=d.Model.Meta.Name+" · "+(d.World==null?"独立房间":d.Placement==null?"尚未放置":"原点 "+d.Origin)+"\n"+Panel.Status;
            }
            status.text = host.HoverFeedback ?? host.AppearanceIssue ?? (string.IsNullOrEmpty(Panel.Status) ? LayoutPanel.DefaultStatus : Panel.Status);
            if (checkOverlay.activeSelf) RefreshIssues();
            RefreshMust();
            RefreshTools();
        }
    }
}
