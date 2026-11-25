/**
 * @file RuntimeLayoutUI.cs
 * @brief Act 把场景按钮的操作交给布局会话；Tool 选择素材或移动工具，ApplyPoint 执行手动输入的世界格命令。
 * LateUpdate 刷新布局、文件及确认面板；RefreshEntries 按目录或房间清单生成可滚动按钮；
 * AddPaletteCard 生成带缩略图的素材和功能物件卡片。绑定状态和目标来自共享草稿命令。
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
        public GameObject solutionDialog;
        public Text solutionText;
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
        bool custom, categoryOpen, workspaceOpen,terrain;
        string[] listedEntries;
        string[] roomIds;
        bool lastDialog;
        string activeRoom;
        public bool CoversMap => workspaceOpen || Panel.ShowingSolution;
        LayoutPanel Panel => host.Panel;
        Color SelectedTint => interfaceStyle && interfaceStyle.theme ? interfaceStyle.theme.active : new Color32(255, 195, 104, 255);
        Color NormalTint => interfaceStyle && interfaceStyle.theme ? interfaceStyle.theme.parchment : new Color32(255, 228, 182, 255);

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
        }

        void SetToolLabel(RoomTool type, string name) => toolButtons[(int)type].GetComponentInChildren<Text>().text = name;

        void OnDestroy() => thumbnails.Dispose();

        async void PreviewCard(Button button,JObject room,string key,bool builtin=false)
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
            var assets = custom ? (draft.Room["assets"] as JObject)?.Properties().Select(entry => entry.Name).ToArray() ?? System.Array.Empty<string>()
                : categoryOpen ? draft.CategoryAssets(paletteCategory).ToArray() : System.Array.Empty<string>();
            var definitions = custom ? draft.Room["objectDefinitions"] as JObject : null;
            string theme = (string)draft.Room["themeId"];
            string key = draft.ActiveId + theme + custom + categoryOpen + paletteCategory + definitions + string.Join(",", assets);
            if (paletteKey != key)
            {
                paletteKey = key;
                foreach (var button in cards.Values) { button.gameObject.SetActive(false); Destroy(button.gameObject); }
                cards.Clear();
                if (pickedAsset != null && draft.AssetDefinition(pickedAsset) == null) pickedAsset = null;
                if (categoryOpen)
                {
                    AddPaletteCard("back", "返回分类", null, () => { categoryOpen = false; pickedAsset = pickedBuiltin = null; Panel.CancelTool(); });
                    AddPaletteCard("import", "导入" + AppearanceLibrary.TypeName(paletteCategory) + "模型", null,
                        () => { pickedAsset = pickedBuiltin = null; Tool(paletteCategory); Panel.ChooseFile("导入模型"); });
                    foreach (var entry in AppearanceLibrary.Entries(paletteCategory, theme))
                        AddPaletteCard(entry.Id, entry.Name, entry.Prefab, () =>
                        {
                            Panel.CancelTool();
                            pickedAsset = null;
                            pickedBuiltin = entry.Id;
                            Panel.Run(() => draft.ChooseAppearance(entry.Id, paletteCategory), "已选 " + entry.Name + "，在地图放置");
                        }, true);
                }
                else if (custom)
                    AddPaletteCard("import", "导入装饰模型", null, () => { Panel.CancelTool(); Panel.ChooseFile("导入模型"); });
                if (definitions != null)
                    foreach (var entry in definitions.Properties())
                    {
                        string id = entry.Name;
                        string model = (string)entry.Value["visual"]["model"];
                        AddPaletteCard(id, (string)entry.Value["name"] + " · 功能物件", model, () =>
                        {
                            Panel.CancelTool();
                            pickedAsset = pickedBuiltin = null;
                            string issue = thumbnails.ModelIssue(draft.Room, model);
                            if (issue != null) Panel.Status = issue;
                            else Panel.Run(() => draft.SelectDefinition(id), "左键放置功能物件，短按右键取消");
                        });
                    }
                foreach (string id in assets)
                {
                    var asset = draft.AssetDefinition(id);
                    string kind = (string)asset["kind"];
                    string label = kind == "model" ? "" : kind == "animation" ? " · 动作" : kind == "texture" ? " · 贴图" : " · 界面图";
                    AddPaletteCard(id, (string)asset["name"] + label, id, () =>
                    {
                        Panel.CancelTool();
                        pickedAsset = id;
                        pickedBuiltin = null;
                        if (kind != "model") return;
                        string issue = thumbnails.ModelIssue(draft.AssetRoom(id), id);
                        if (issue != null) Panel.Status = issue;
                        else Panel.Run(() => draft.SelectAsset(id, custom ? RoomTool.Decoration : paletteCategory), "左键放置，短按右键取消");
                    });
                }
            }
            bindAsset.gameObject.SetActive((custom || categoryOpen) && (pickedAsset != null || pickedBuiltin != null));
            if (pickedBuiltin != null)
                bindAsset.interactable = draft.CanReplaceAppearance(pickedBuiltin);
            else if (pickedAsset != null)
                bindAsset.interactable = draft.CanBindAsset(pickedAsset, categoryOpen ? paletteCategory : RoomTool.Select) &&
                    thumbnails.ModelIssue(draft.AssetRoom(pickedAsset), pickedAsset) == null;
            foreach (var card in cards) card.Value.image.color = card.Key == (draft.BrushDefinition ?? draft.BrushBuiltin ?? draft.BrushAsset ?? pickedBuiltin ?? pickedAsset)
                ? SelectedTint : NormalTint;
        }

        void AddPaletteCard(string id, string title, string model, UnityEngine.Events.UnityAction action, bool builtin = false)
        {
            var button = Instantiate(entryPrefab, assetEntries, false);
            cards.Add(id, button);
            var label = button.GetComponentInChildren<Text>();
            label.text = title;
            button.onClick.AddListener(action);
            if (model == null) return;
            label.rectTransform.offsetMin = new Vector2(36, label.rectTransform.offsetMin.y);
            var thumb = new GameObject("缩略图", typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
            thumb.rectTransform.SetParent(button.transform, false);
            thumb.rectTransform.anchorMin = thumb.rectTransform.anchorMax = new Vector2(0, .5f);
            thumb.rectTransform.anchoredPosition = new Vector2(20, 0);
            thumb.rectTransform.sizeDelta = new Vector2(30, 30);
            thumb.raycastTarget = false;
            thumb.enabled = false;
            PreviewCard(button, Panel.Draft.AssetRoom(builtin ? null : model), model, builtin);
        }

        void OpenCategory(RoomTool type)
        {
            custom = false;
            categoryOpen = true;
            paletteCategory = type;
            pickedAsset = pickedBuiltin = null;
            Panel.CancelTool();
        }
        public void Act(string action)
        {
            var d=Panel.Draft;
            switch(action)
            {
                case "文件": fileMenu.SetActive(!fileMenu.activeSelf);return;
                case "保存": case "打开": case "导入": case "导出": case "发布": case "导出单房": case "导入模型": case "导入动作": case "导入贴图": case "导入界面图": Panel.ChooseFile(action);break;
                case "新建": Panel.NewRoom(true);break;
                case "默认布局": Panel.NewDefaultRoom();break;
                case "撤销": Panel.Run(d.Undo,"已撤销");break;
                case "重做": Panel.Run(d.Redo,"已重做");break;
                case "网格": host.GridVisible=!host.GridVisible;break;
                case "试玩": fileMenu.SetActive(false); host.StartTrial(); return;
                case "离开": host.Leave();break;
                case "工作区": workspaceOpen=true;break;
                case "返回": workspaceOpen=false;break;
                case "选物件": custom = categoryOpen = false; terrain = false; pickedAsset = pickedBuiltin = null; break;
                case "选地形": custom = categoryOpen = false; terrain = true; pickedAsset = pickedBuiltin = null; break;
                case "选素材":
                    custom = true; categoryOpen = false; pickedAsset = pickedBuiltin = null; break;
                case "绑定素材":
                    Panel.Run(() =>
                    {
                        if (pickedBuiltin != null) d.ReplaceAppearance(pickedBuiltin);
                        else d.BindAsset(pickedAsset, categoryOpen ? paletteCategory : RoomTool.Select);
                        Panel.CancelTool();
                    }, "已更新外观绑定");
                    break;
                case "切换房间视图": host.ToggleRoomView();workspaceOpen=false;break;
                case "选择": Tool(RoomTool.Select);break;
                case "纸箱": OpenCategory(RoomTool.Box); break;
                case "目标": OpenCategory(RoomTool.Target); break;
                case "墙壁": OpenCategory(RoomTool.Wall); break;
                case "地板": OpenCategory(RoomTool.Floor); break;
                case "擦除": Tool(RoomTool.Erase);break;
                case "移动": Tool(RoomTool.Move);break;
                case "建世界": Panel.Run(d.BeginWorld,"世界已创建，请放置房间");break;
                case "加入房间": Panel.ChooseFile(action);break;
                case "整房预览": if(d.World!=null){Panel.StartRoomPlacement();workspaceOpen=false;}break;
                case "按坐标放置": ApplyPoint(true);break;
                case "移动对象": ApplyPoint(false);break;
                case "移出": Panel.Run(d.RemoveRoom,"房间已移回目录");break;
                case "名称": Panel.Run(()=>d.Rename(roomName.text),"已修改名称");break;
                case "关闭检查": Panel.CloseSolution(); break;
                case "检查": Panel.CheckCurrentRoom();break;
                case "上一级": Panel.ParentDirectory();break;
                case "提交文件": Panel.SubmitFile();break;
                case "取消": Panel.CancelDialog();break;
                case "保存继续": Panel.ContinueAfterSave();break;
                case "放弃": Panel.DiscardAndContinue();break;
                case "保留": Panel.KeepAndExit();break;
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
            var point=new Vector2Int(x,y);var d=Panel.Draft;
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
                string label=directory?(Directory.Exists(item)?"▸  ":"")+Path.GetFileName(item):(string)Panel.Draft.Catalog[item]["name"]+"   "+item;
                button.GetComponentInChildren<Text>().text=label;
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
            if (!directory && Panel.Draft.Selection.Count > 0)
            {
                var nameField = Instantiate(roomName, container, false);
                nameField.name = "选中对象名称";
                bool same = Panel.Draft.TryGetSelectedName(out var name);
                nameField.SetTextWithoutNotify(name);
                if (nameField.placeholder is Text placeholder) placeholder.text = same ? "对象名称" : "所选名称不同";
                var apply = Instantiate(entryPrefab, container, false);
                apply.GetComponentInChildren<Text>().text = "应用对象名称到所选 " + Panel.Draft.Selection.Count + " 格";
                apply.onClick.AddListener(() => Panel.Run(() => Panel.Draft.SetSelectedName(nameField.text), "已修改选中对象名称"));
            }
            if (!directory && Panel.Draft.SelectedCell.HasValue)
                for (int i = 0; i < LayoutPanel.MarkerLabels.Length; i++)
                {
                    int index = i;
                    var button = Instantiate(entryPrefab, container, false);
                    button.GetComponentInChildren<Text>().text = LayoutPanel.MarkerLabels[index];
                    button.onClick.AddListener(() => { Panel.ApplyMarker(index); workspaceOpen = false; });
                }
            if (!directory && Panel.Draft.World != null)
                foreach (var link in Panel.Draft.World["connections"])
                {
                    var button = Instantiate(entryPrefab, container, false);
                    button.GetComponentInChildren<Text>().text = Panel.ConnectionAction(link);
                    button.onClick.AddListener(() => Panel.ToggleConnection(link));
                }
            var scroll=container.GetComponentInParent<ScrollRect>();
            if(scroll)scroll.verticalNormalizedPosition=1;
        }
        void ChooseEntry(string item,bool directory)
        {
            if(directory)Panel.PickEntry(item);
            else {Panel.CancelTool();Panel.Draft.UseRoom(item);}
        }

        void RefreshTools()
        {
            var draft=Panel.Draft;
            objectCards.SetActive(!custom && !categoryOpen && !terrain);
            terrainCards.SetActive(!custom && !categoryOpen && terrain);
            assetList.SetActive(custom || categoryOpen);
            RefreshAssets();
            Color selected = SelectedTint, normal = NormalTint;
            objectTab.image.color=!custom && !terrain?selected:normal;terrainTab.image.color=!custom && terrain?selected:normal;assetTab.image.color=custom?selected:normal;
            for(int i=0;i<toolButtons.Length;i++)
                if(toolButtons[i])toolButtons[i].image.color=(int)draft.Tool==i?selected:normal;
            var obj=draft.Find(draft.Selected);
            if (custom && draft.BrushDefinition != null)
            {
                properties.text = (string)draft.Room["objectDefinitions"][draft.BrushDefinition]["name"] + "\n每次放置都会创建独立行为。";
                return;
            }
            if ((custom || categoryOpen) && pickedAsset != null)
            {
                var asset = draft.AssetDefinition(pickedAsset);
                properties.text = (string)asset["name"] + "  " + thumbnails.SizeReport(pickedAsset) + "\n" + draft.AssetBindingTarget(pickedAsset);
                return;
            }
            if (categoryOpen && pickedBuiltin != null)
            {
                var look = AppearanceLibrary.Find(pickedBuiltin);
                properties.text = look.Name + "\n" + AppearanceLibrary.TypeName(look.Type);
                return;
            }
            properties.text=draft.Tool==RoomTool.Box?"箱子\n在空地上放置一个箱子。"
                :draft.Tool==RoomTool.Target?"目标点\n标记箱子需要到达的格子。"
                :obj!=null?((string)obj["name"] == null ? "" : (string)obj["name"] + "\n")+draft.Selected+"\n当前格 "+RoomFile.At(obj)+(obj["recoveryCell"]!=null?"\n恢复格 "+RoomFile.Cell(obj["recoveryCell"]):"")
                :(string)draft.Room["name"]+"\n"+RoomFile.Size(draft.Room);
        }

        void LateUpdate()
        {
            if(!host || Panel==null)return;
            solutionDialog.SetActive(Panel.ShowingSolution);
            if (Panel.ShowingSolution)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) Panel.CloseSolution();
                solutionText.text = Panel.SolutionText;
                solutionDialog.transform.SetAsLastSibling();
            }
            bool dialog=Panel.Operation!=null && !Panel.Confirming;
            fileDialog.SetActive(dialog);
            confirmation.SetActive(Panel.Confirming);
            keepDraft.SetActive(Panel.CanKeepDraft);
            workspace.SetActive(workspaceOpen && !Panel.Modal);
            if(dialog)
            {
                dialogTitle.text=Panel.Operation;
                pathText.text=Panel.DirectoryPath;
                if(!lastDialog || !ReferenceEquals(listedEntries,Panel.Entries))
                {
                    listedEntries=Panel.Entries;
                    RefreshEntries(entries,listedEntries,true);
                }
                if(fileName.text!=Panel.Filename)fileName.SetTextWithoutNotify(Panel.Filename??"");
                submitText.text=Panel.Overwrite?"确认覆盖":Panel.Operation;
                message.text=Panel.Status;
            }
            lastDialog=dialog;
            if(Panel.Confirming)confirmMessage.text=Panel.Status;
            if(workspace.activeSelf)
            {
                var d=Panel.Draft;
                var ids=d.Catalog.Properties().Select(p=>p.Name).ToArray();
                if(listedRevision != d.Revision || roomIds==null || !roomIds.SequenceEqual(ids) || activeRoom!=d.ActiveId)
                {
                    RefreshEntries(rooms,ids,false);
                    listedRevision=d.Revision;roomIds=ids;activeRoom=d.ActiveId;
                    roomName.SetTextWithoutNotify((string)d.Room["name"]);
                }
                workspaceInfo.text=(string)d.Room["name"]+" · "+(d.World==null?"独立房间":d.Placement==null?"尚未放置":"原点 "+d.Origin)+"\n"+(Panel.StructuralIssue??Panel.Status);
            }
            status.text = host.HoverFeedback ?? host.AppearanceIssue ?? Panel.StructuralIssue ?? Panel.Status;
            RefreshTools();
        }
    }
}
