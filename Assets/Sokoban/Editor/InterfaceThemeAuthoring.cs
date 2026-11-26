/**
 * @file InterfaceThemeAuthoring.cs
 * @brief Install creates the shared theme and eight uGUI prefab templates, then links the production pages to the theme.
 * CreateTemplate uses native controls and retains the existing material card and list entry as editable layout sources.
 * RefreshPaletteTabs replaces the scene's mixed button sources with instances of Bookmark.prefab.
 * ApplyUiPackage 是 UI 交付包的批处理入口：给主菜单场景与 PuzzleSession 预制件标记 ThemeRole，调整页面宽度，换主菜单图标，补错误面板警告图标，重新 Capture 并套用主题。
 * Verify checks page and prefab references, live theme propagation, control states and unchanged authored baselines.
 */
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban.Editor
{
    public static class InterfaceThemeAuthoring
    {
        const string Root = "Assets/Sokoban/Resources/LayoutUI/";
        const string ThemePath = Root + "WarmPixel.asset";
        const string BookmarkPath = Root + "Bookmark.prefab";
        const string PuzzlePath = "Assets/Sokoban/Resources/PuzzleSession.prefab";
        const string MenuPath = "Assets/Scenes/MainMenu.unity";
        const string EditorPath = "Assets/Scenes/RuntimeEditor.unity";
        static readonly string[] Templates = { "Button", "Input", "Dropdown", "Bookmark", "MaterialCard", "ScrollList", "Dialog", "Tooltip" };

        public static void Install()
        {
            var theme = AssetDatabase.LoadAssetAtPath<InterfaceTheme>(ThemePath);
            if (!theme)
            {
                theme = ScriptableObject.CreateInstance<InterfaceTheme>();
                theme.controlFrame = CreateFrame();
                AssetDatabase.CreateAsset(theme, ThemePath);
            }
            foreach (var name in Templates) CreateTemplate(name, theme);
            EditPrefab(Root + "ListEntry.prefab", prefab => Link(prefab, theme).Apply());
            EditPrefab(PuzzlePath, prefab => LinkPage(prefab.GetComponentInChildren<Canvas>(true).gameObject, theme));

            var menu = EditorSceneManager.OpenScene(MenuPath);
            foreach (var canvas in menu.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Canvas>(true)).Where(canvas => canvas.isRootCanvas))
                LinkPage(canvas.gameObject, theme);
            EditorSceneManager.SaveScene(menu);

            var editor = EditorSceneManager.OpenScene(EditorPath);
            var layout = editor.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RuntimeLayoutUI>(true)).Single();
            var surface = layout.GetComponentInParent<Canvas>();
            if (!surface) surface = editor.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Canvas>(true)).Single(canvas => canvas.isRootCanvas);
            MatchPaletteTabs(layout);
            LinkPage(surface.gameObject, theme);
            layout.interfaceStyle = surface.GetComponent<InterfaceThemeScope>();
            AddTooltips(surface);
            EditorSceneManager.SaveScene(editor);
            AssetDatabase.SaveAssets();
            Verify();
        }

        public static void RefreshPaletteTabs()
        {
            var theme = AssetDatabase.LoadAssetAtPath<InterfaceTheme>(ThemePath);
            CreateTemplate("Bookmark", theme);
            var scene = EditorSceneManager.OpenScene(EditorPath);
            var layout = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<RuntimeLayoutUI>(true)).Single();
            MatchPaletteTabs(layout);
            layout.interfaceStyle.Capture();
            layout.interfaceStyle.Apply();
            EditorSceneManager.SaveScene(scene);
        }

        static void MatchPaletteTabs(RuntimeLayoutUI layout)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BookmarkPath);
            layout.objectTab = MakeTab(layout.objectTab, prefab, layout, "物件", "选物件");
            layout.terrainTab = MakeTab(layout.terrainTab, prefab, layout, "地形", "选地形");
            layout.assetTab = MakeTab(layout.assetTab, prefab, layout, "自定义", "选素材");
        }

        static Button MakeTab(Button previous, GameObject prefab, RuntimeLayoutUI layout, string title, string action)
        {
            if (PrefabUtility.GetCorrespondingObjectFromSource(previous.gameObject) == prefab) return previous;
            var previousRect = (RectTransform)previous.transform;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, previousRect.parent);
            instance.name = title;
            var rect = (RectTransform)instance.transform;
            rect.SetSiblingIndex(previousRect.GetSiblingIndex());
            rect.anchorMin = previousRect.anchorMin;
            rect.anchorMax = previousRect.anchorMax;
            rect.anchoredPosition = previousRect.anchoredPosition;
            var label = instance.GetComponentInChildren<Text>();
            label.text = title;
            var button = instance.GetComponent<Button>();
            UnityEditor.Events.UnityEventTools.AddStringPersistentListener(button.onClick, layout.Act, action);
            foreach (var item in new UnityEngine.Object[] { instance, rect, label, button })
                PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            UnityEngine.Object.DestroyImmediate(previous.gameObject);
            return button;
        }

        static void ConfigureBookmark(GameObject root)
        {
            ((RectTransform)root.transform).sizeDelta = new Vector2(68, 38);
            var outline = root.GetComponent<Outline>();
            if (outline) UnityEngine.Object.DestroyImmediate(outline, true);
            var label = root.GetComponentInChildren<Text>();
            label.fontSize = 18;
            label.alignment = TextAnchor.MiddleCenter;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(2, 0);
            label.rectTransform.offsetMax = new Vector2(-2, 0);
        }

        static Sprite CreateFrame()
        {
            const int size = 20;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int dx = Mathf.Min(x, size - 1 - x), dy = Mathf.Min(y, size - 1 - y);
                    Color32 pixel = Color.white;
                    if (dx + dy < 3) pixel = new Color32(0, 0, 0, 0);
                    else if (dx == 0 || dy == 0 || dx + dy == 3) pixel = new Color32(137, 100, 82, 255);
                    else if (y <= 2) pixel = new Color32(199, 154, 119, 255);
                    else if (dx <= 1 || dy <= 1) pixel = new Color32(229, 200, 176, 255);
                    texture.SetPixel(x, y, pixel);
                }
            string path = Root + "WarmFrame.png";
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100;
            importer.spriteBorder = new Vector4(5, 5, 5, 5);
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        static void EditPrefab(string path, Action<GameObject> edit)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { edit(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static InterfaceThemeScope Link(GameObject root, InterfaceTheme theme)
        {
            var style = root.GetComponent<InterfaceThemeScope>();
            if (!style)
            {
                style = root.AddComponent<InterfaceThemeScope>();
                style.Capture();
            }
            else if (theme.fontSizeAdjustment == 0 && theme.spacingAdjustment == 0) style.Capture();
            style.theme = theme;
            return style;
        }

        static void LinkPage(GameObject root, InterfaceTheme theme)
        {
            var style = Link(root, theme);
            style.panels = root.GetComponentsInChildren<Image>(true).Where(image => image.name == "面板" && !image.GetComponent<Selectable>()).ToArray();
            style.Apply();
        }

        static void CreateTemplate(string name, InterfaceTheme theme)
        {
            string path = Root + name + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path))
            {
                EditPrefab(path, root =>
                {
                    if (name == "Dialog") ConnectDialog(root);
                    if (name == "Bookmark") ConfigureBookmark(root);
                    ApplyTemplateTheme(root, theme);
                });
                return;
            }
            var resources = new DefaultControls.Resources
            {
                standard = theme.controlFrame, background = theme.controlFrame, inputField = theme.controlFrame,
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd")
            };
            GameObject root;
            switch (name)
            {
                case "Input": root = DefaultControls.CreateInputField(resources); break;
                case "Dropdown": root = DefaultControls.CreateDropdown(resources); break;
                case "ScrollList": root = DefaultControls.CreateScrollView(resources); break;
                case "Dialog": case "Tooltip": root = CreatePanel(name, resources); break;
                default: root = DefaultControls.CreateButton(resources); break;
            }
            root.name = name;
            UseChineseLabels(root);
            if (name == "Bookmark") ConfigureBookmark(root);
            var style = Link(root, theme);
            if (name == "Dialog" || name == "Tooltip" || name == "ScrollList") style.panels = new[] { root.GetComponent<Image>() };
            style.Apply();
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
        }

        static void ApplyTemplateTheme(GameObject root, InterfaceTheme theme)
        {
            UseChineseLabels(root);
            Link(root, theme).Apply();
        }

        static void UseChineseLabels(GameObject root)
        {
            var input = root.GetComponent<InputField>();
            var dropdown = root.GetComponent<Dropdown>();
            foreach (var old in root.GetComponentsInChildren<Text>(true).Where(text => !(text is LayoutText)))
            {
                bool inputText = input && input.textComponent == old, placeholder = input && input.placeholder == old;
                bool caption = dropdown && dropdown.captionText == old, item = dropdown && dropdown.itemText == old;
                var target = old.gameObject;
                string value = old.text;
                int size = old.fontSize;
                var alignment = old.alignment;
                bool raycast = old.raycastTarget;
                bool rich = old.supportRichText;
                var style = old.fontStyle;
                UnityEngine.Object.DestroyImmediate(old);
                var replacement = target.AddComponent<LayoutText>();
                replacement.text = value;
                replacement.fontSize = size;
                replacement.alignment = alignment;
                replacement.raycastTarget = raycast;
                replacement.supportRichText = rich;
                replacement.fontStyle = style;
                if (inputText) input.textComponent = replacement;
                if (placeholder) input.placeholder = replacement;
                if (caption) dropdown.captionText = replacement;
                if (item) dropdown.itemText = replacement;
            }
        }

        static GameObject CreatePanel(string name, DefaultControls.Resources resources)
        {
            bool dialog = name == "Dialog";
            var root = new GameObject(name, typeof(RectTransform), typeof(Image));
            var image = root.GetComponent<Image>();
            image.raycastTarget = dialog;
            var rect = image.rectTransform;
            rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = dialog ? new Vector2(360, 156) : new Vector2(218, 56);
            var label = new GameObject("说明", typeof(RectTransform), typeof(LayoutText)).GetComponent<LayoutText>();
            label.transform.SetParent(rect, false);
            label.alignment = dialog ? TextAnchor.UpperLeft : TextAnchor.MiddleLeft;
            label.fontSize = dialog ? 19 : 15;
            label.raycastTarget = false;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(12, dialog ? 48 : 5);
            label.rectTransform.offsetMax = new Vector2(-12, -8);
            if (dialog)
            {
                var button = DefaultControls.CreateButton(resources);
                button.transform.SetParent(rect, false);
                ((RectTransform)button.transform).anchoredPosition = new Vector2(0, -49);
                button.GetComponentInChildren<Text>().text = "关闭";
                ConnectDialog(root);
            }
            return root;
        }

        static void ConnectDialog(GameObject root)
        {
            var close = root.GetComponentInChildren<Button>(true);
            if (close && close.onClick.GetPersistentEventCount() == 0)
                UnityEditor.Events.UnityEventTools.AddBoolPersistentListener(close.onClick, root.SetActive, false);
        }

        static void AddTooltips(Canvas canvas)
        {
            var tips = new System.Collections.Generic.Dictionary<string, string>
            {
                ["保存"] = "保存当前房间或世界到工作文件。",
                ["网格"] = "显示或隐藏地图上的 1×1 编辑网格。",
                ["试玩"] = "从当前布局开始独立试玩，退出后返回草稿。"
            };
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Tooltip.prefab");
            foreach (var button in canvas.GetComponentsInChildren<Button>(true))
            {
                var label = button.GetComponentInChildren<Text>();
                if (!label || !tips.TryGetValue(label.text, out var hint) || button.GetComponent<InterfaceTooltip>()) continue;
                var tooltip = (GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform);
                tooltip.SetActive(false);
                var target = button.gameObject.AddComponent<InterfaceTooltip>();
                target.tooltip = (RectTransform)tooltip.transform;
                target.label = tooltip.GetComponentInChildren<Text>(true);
                target.description = hint;
            }
        }

        public static void Verify()
        {
            var theme = AssetDatabase.LoadAssetAtPath<InterfaceTheme>(ThemePath);
            Require(theme && theme.controlFrame, "主题及九切片");
            foreach (var name in Templates)
            {
                var template = AssetDatabase.LoadAssetAtPath<GameObject>(Root + name + ".prefab");
                Require(template && template.GetComponent<InterfaceThemeScope>().theme == theme, name + " 模板主题引用");
            }
            var menu = EditorSceneManager.OpenScene(MenuPath).GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<InterfaceThemeScope>(true)).First();
            var play = PrefabUtility.LoadPrefabContents(PuzzlePath);
            try
            {
                var panels = play.GetComponentInChildren<PlayPanels>(true);
                var scope = play.GetComponentInChildren<InterfaceThemeScope>(true);
                foreach (var page in new[] { panels.hud, panels.settings, panels.pause })
                    Require(page && page.transform.IsChildOf(scope.transform) && scope.theme == theme, page.name + " 主题接入");
                var editor = EditorSceneManager.OpenScene(EditorPath, OpenSceneMode.Additive).GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<InterfaceThemeScope>(true)).First(scope => scope.GetComponent<Canvas>());
                int original = theme.fontSizeAdjustment;
                float spacing = theme.spacingAdjustment;
                var color = theme.parchment;
                var samples = new[] { menu.gameObject, panels.hud, panels.settings, panels.pause, editor.gameObject };
                var labels = samples.Select(page => page.GetComponentInChildren<Text>(true)).ToArray();
                var sizes = labels.Select(label => label.fontSize).ToArray();
                var stacks = new[] { menu, scope, editor }.SelectMany(root => root.GetComponentsInChildren<HorizontalOrVerticalLayoutGroup>(true)).ToArray();
                var spaces = stacks.Select(stack => stack.spacing).ToArray();
                try
                {
                    theme.fontSizeAdjustment += 2;
                    theme.spacingAdjustment += 3;
                    theme.parchment = Color.magenta;
                    foreach (var sheet in new[] { menu, scope, editor }) sheet.Apply();
                    for (int i = 0; i < samples.Length; i++) Require(labels[i].fontSize == sizes[i] + 2, samples[i].name + " 字号传播");
                    Require(stacks.Length > 0 && stacks.Select((stack, i) => Mathf.Approximately(stack.spacing, spaces[i] + 3)).All(value => value), "布局间距传播");
                    foreach (var page in samples)
                    {
                        var button = page.GetComponentInChildren<Button>(true);
                        if (button) Require(button.targetGraphic.color == Color.magenta && button.colors.disabledColor == theme.disabled, page.name + " 按钮色和禁用状态传播");
                    }
                }
                finally
                {
                    theme.fontSizeAdjustment = original;
                    theme.spacingAdjustment = spacing;
                    theme.parchment = color;
                    foreach (var sheet in new[] { menu, scope, editor }) sheet.Apply();
                }
            }
            finally { PrefabUtility.UnloadPrefabContents(play); }
        }

        static void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("UIV-001 " + label);
            Debug.Log("UIV-001 PASS " + label);
        }

        public static void ApplyUiPackage()
        {
            var theme = AssetDatabase.LoadAssetAtPath<InterfaceTheme>(ThemePath);
            if (!theme) throw new InvalidOperationException("主题资源缺失");
            var menu = EditorSceneManager.OpenScene(MenuPath);
            foreach (var canvas in menu.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Canvas>(true)).Where(canvas => canvas.isRootCanvas))
            {
                MarkRoles(canvas.gameObject, canvas.GetComponentInChildren<PlayPanels>(true));
                LayoutMainMenu(canvas.gameObject, theme);
                LayoutPlayPages(canvas.gameObject, theme);
                LinkPage(canvas.gameObject, theme);
                StyleErrorPanel(canvas.gameObject, theme);
                canvas.GetComponent<InterfaceThemeScope>().Capture();
                canvas.GetComponent<InterfaceThemeScope>().Apply();
            }
            EditorSceneManager.SaveScene(menu);
            EditPrefab(PuzzlePath, prefab =>
            {
                var page = prefab.GetComponentInChildren<Canvas>(true).gameObject;
                MarkRoles(page, page.GetComponentInChildren<PlayPanels>(true));
                LayoutPlayPages(page, theme);
                LinkPage(page, theme);
                StyleErrorPanel(page, theme);
                var scope = page.GetComponent<InterfaceThemeScope>();
                scope.Capture();
                scope.Apply();
            });
            PrepareTemplateRoles(theme);
            AssetDatabase.SaveAssets();
            Debug.Log("UIPACK applied");
        }

        static void PrepareTemplateRoles(InterfaceTheme theme)
        {
            var kinds = new System.Collections.Generic.Dictionary<string, ThemeRoleKind>
            {
                ["Button"] = ThemeRoleKind.Body, ["Bookmark"] = ThemeRoleKind.Bookmark, ["MaterialCard"] = ThemeRoleKind.Card
            };
            foreach (var pair in kinds)
                EditPrefab(Root + pair.Key + ".prefab", root =>
                {
                    var role = root.GetComponent<ThemeRole>() ?? root.AddComponent<ThemeRole>();
                    role.kind = pair.Value;
                    var scope = Link(root, theme);
                    scope.Capture();
                    scope.Apply();
                });
        }

        static Text TitleOf(GameObject page) =>
            page ? page.GetComponentsInChildren<Text>(true).Where(text => !text.GetComponentInParent<Button>(true)).OrderByDescending(text => text.fontSize).FirstOrDefault() : null;

        static void SetTitle(GameObject page, string value)
        {
            var title = TitleOf(page);
            if (!title) return;
            title.text = value;
            (title.GetComponent<ThemeRole>() ?? title.gameObject.AddComponent<ThemeRole>()).kind = ThemeRoleKind.Title;
            title.raycastTarget = false;
        }

        static void MarkRoles(GameObject root, PlayPanels panels)
        {
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                var role = button.GetComponent<ThemeRole>() ?? button.gameObject.AddComponent<ThemeRole>();
                string title = button.name;
                bool inPage = panels && (Under(button, panels.pause) || Under(button, panels.settings) || Under(button, panels.help) || Under(button, panels.discovery) || Under(button, panels.error));
                role.kind = title == "退出游戏" ? ThemeRoleKind.Danger : inPage || title == "覆盖并开始" ? ThemeRoleKind.Primary : ThemeRoleKind.Body;
            }
            foreach (var text in root.GetComponentsInChildren<Text>(true))
                if (text.name == "标题" || text.name == "暂停" || text.name == "设置" && text.rectTransform.sizeDelta.y > 40 || text.name == "主菜单")
                    (text.GetComponent<ThemeRole>() ?? text.gameObject.AddComponent<ThemeRole>()).kind = ThemeRoleKind.Title;
        }

        static bool Under(Component item, GameObject page) => page && item.transform.IsChildOf(page.transform);

        static void LayoutMainMenu(GameObject root, InterfaceTheme theme)
        {
            var panels = root.GetComponentInChildren<PlayPanels>(true);
            if (!panels || !panels.menu || !panels.menu.continueButton) return;
            var board = (RectTransform)panels.menu.continueButton.transform.parent;
            var shell = board.parent as RectTransform;
            var panel = board.Find("面板") as RectTransform ?? shell;
            foreach (var node in root.GetComponentsInChildren<RectTransform>(true))
            {
                if (node.name == "面板" && node.sizeDelta.y >= 480) node.sizeDelta = new Vector2(380 * PixelScale, node.sizeDelta.y);
                else if (node.name == "面板阴影" && node.sizeDelta.y >= 480) node.sizeDelta = new Vector2(388 * PixelScale, node.sizeDelta.y);
            }
            foreach (var button in board.GetComponentsInChildren<Button>(true))
            {
                var rect = (RectTransform)button.transform;
                bool pair = button.name == "设置" || button.name == "操作说明";
                rect.sizeDelta = new Vector2(pair ? 115 : 240, rect.sizeDelta.y);
                if (pair) rect.anchoredPosition = new Vector2(button.name == "设置" ? -62.5f : 62.5f, rect.anchoredPosition.y);
            }
            foreach (var old in board.GetComponentsInChildren<Image>(true).Where(image => image.name == "纸箱")) old.gameObject.SetActive(false);
            if (board.Find("徽标")) return;
            var logo = new GameObject("徽标", typeof(RectTransform)).GetComponent<RectTransform>();
            logo.SetParent(board, false);
            logo.anchorMin = logo.anchorMax = new Vector2(.5f, 1);
            logo.sizeDelta = new Vector2(64, 64);
            logo.anchoredPosition = new Vector2(0, -52);
            var colors = new[] { new Color32(176, 123, 74, 255), new Color32(231, 197, 139, 255), new Color32(176, 123, 74, 255) };
            for (int i = 0; i < 3; i++)
            {
                var bar = new GameObject("竖条" + i, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
                bar.transform.SetParent(logo, false);
                bar.sprite = theme.thumbSprite;
                bar.type = Image.Type.Sliced;
                bar.color = colors[i];
                bar.raycastTarget = false;
                var rect = bar.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                rect.sizeDelta = new Vector2(18, 64);
                rect.anchoredPosition = new Vector2((i - 1) * 24, 0);
            }
        }

        static void LayoutPlayPages(GameObject root, InterfaceTheme theme)
        {
            var panels = root.GetComponentInChildren<PlayPanels>(true);
            if (!panels) return;
            SetWidth(panels.pause, 420);
            SetWidth(panels.settings, 410);
            SetWidth(panels.help, 420);
            SetWidth(panels.confirm, 420);
            SetWidth(panels.discovery, 440);
            SetWidth(panels.error, 420);
            SetTitle(panels.pause, "暂停");
            SetTitle(panels.settings, "设置");
            SetTitle(panels.help, "操作说明");
            SetTitle(panels.confirm, "开始新游戏？");
            SetTitle(panels.error, "无法开始");
            SetTitle(panels.discovery, "获得道具");
            if (panels.discovery && !panels.discoveryTitle) panels.discoveryTitle = TitleOf(panels.discovery);
            if (panels.confirm)
            {
                var body = panels.confirm.GetComponentsInChildren<Text>(true).FirstOrDefault(text => text.text.Contains("覆盖") && !text.GetComponentInParent<Button>(true));
                if (body) body.text = "已有游戏进度，开始新游戏会覆盖它。";
            }
            if (panels.error)
            {
                var detail = panels.error.GetComponentsInChildren<Text>(true).Where(text => !text.GetComponentInParent<Button>(true) && text != TitleOf(panels.error)).FirstOrDefault();
                if (detail && string.IsNullOrWhiteSpace(detail.text)) detail.text = "请从主菜单开始游戏，或从编辑器试玩房间。";
            }
            if (panels.hud)
            {
                var seal = panels.hud.GetComponentsInChildren<Image>(true).FirstOrDefault(image => image.name == "封条");
                if (seal)
                {
                    seal.sprite = theme.panelSmallSprite;
                    seal.type = Image.Type.Sliced;
                    seal.color = Color.white;
                    var rect = seal.rectTransform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                    rect.anchoredPosition = new Vector2(20, -20);
                }
            }
            BuildHelpRows(panels.help);
            if (panels.confirm)
            {
                var confirmPanel = panels.confirm.transform.Find("面板") as RectTransform;
                if (confirmPanel) confirmPanel.sizeDelta = new Vector2(confirmPanel.sizeDelta.x, 230);
                var copy = panels.confirm.GetComponentsInChildren<Text>(true).FirstOrDefault(text => !text.GetComponentInParent<Button>(true) && text != TitleOf(panels.confirm));
                if (copy) Anchor(copy.rectTransform, 1, -105);
                var confirmTitle = TitleOf(panels.confirm);
                if (confirmTitle) Anchor(confirmTitle.rectTransform, 1, -50);
                foreach (var button in panels.confirm.GetComponentsInChildren<Button>(true))
                {
                    bool accept = button.name == "覆盖并开始";
                    var label = button.GetComponentInChildren<Text>(true);
                    if (label) label.text = accept ? "确认" : "取消";
                    var rect = (RectTransform)button.transform;
                    Anchor(rect, 0, 42);
                    rect.sizeDelta = new Vector2(115, 44);
                    rect.anchoredPosition = new Vector2(accept ? -62.5f : 62.5f, 42);
                }
            }
            if (panels.error)
            {
                var errorPanel = panels.error.transform.Find("面板") as RectTransform;
                if (errorPanel)
                {
                    var icon = panels.error.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(item => item.name == "警告图标") ?? ThemeWidgets.WarningIcon(errorPanel);
                    icon.SetParent(errorPanel, false);
                    icon.SetAsFirstSibling();
                    Anchor(icon, 1, -50);
                    if (icon.sizeDelta.x < 1) icon.sizeDelta = new Vector2(64, 64);
                    errorPanel.sizeDelta = new Vector2(errorPanel.sizeDelta.x, 290);
                    Anchor(TitleOf(panels.error).rectTransform, 1, -118);
                    var lead = panels.error.GetComponentsInChildren<Text>(true).FirstOrDefault(text => !text.GetComponentInParent<Button>(true) && text != TitleOf(panels.error));
                    if (lead) Anchor(lead.rectTransform, 1, -165);
                    var back = panels.error.GetComponentInChildren<Button>(true);
                    if (back) Anchor((RectTransform)back.transform, 0, 40);
                }
            }
        }

        const float PixelScale = 1.083f;

        static void SetWidth(GameObject page, float width)
        {
            if (!page) return;
            var panel = page.transform.Find("面板") as RectTransform;
            if (panel) panel.sizeDelta = new Vector2(width * PixelScale, panel.sizeDelta.y);
        }

        static void Anchor(RectTransform rect, float edge, float y)
        {
            var size = rect.rect.size;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, edge);
            rect.sizeDelta = size;
            rect.anchoredPosition = new Vector2(0, y);
        }

        static void BuildHelpRows(GameObject help)
        {
            if (!help) return;
            var panel = help.transform.Find("面板") as RectTransform;
            if (!panel || panel.Find("按键列表")) return;
            var title = TitleOf(help);
            var body = help.GetComponentsInChildren<Text>(true).FirstOrDefault(text => !text.GetComponentInParent<Button>(true) && text != title);
            if (!body) return;
            body.gameObject.SetActive(false);
            panel.sizeDelta = new Vector2(panel.sizeDelta.x, 340);
            var list = new GameObject("按键列表", typeof(RectTransform), typeof(VerticalLayoutGroup)).GetComponent<RectTransform>();
            list.SetParent(panel, false);
            list.anchorMin = list.anchorMax = new Vector2(.5f, 1);
            list.sizeDelta = new Vector2(320, 170);
            list.anchoredPosition = new Vector2(0, -180);
            var stack = list.GetComponent<VerticalLayoutGroup>();
            stack.childControlWidth = stack.childControlHeight = true;
            stack.childForceExpandWidth = stack.childForceExpandHeight = true;
            stack.spacing = 4;
            foreach (var pair in new[] { ("WASD", "移动，始终对应屏幕方向"), ("Q / E", "90° 转向镜头"), ("滚轮", "切换已获得的道具"), ("R", "使用选中的道具"), ("Esc", "暂停") })
            {
                var row = new GameObject(pair.Item1, typeof(RectTransform), typeof(HorizontalLayoutGroup)).GetComponent<RectTransform>();
                row.SetParent(list, false);
                var line = row.GetComponent<HorizontalLayoutGroup>();
                line.childControlWidth = line.childControlHeight = true;
                line.childForceExpandWidth = line.childForceExpandHeight = true;
                line.spacing = 16;
                AddHelpText(row, pair.Item1, body, true, 90);
                AddHelpText(row, pair.Item2, body, false, 230);
            }
            var back = help.GetComponentInChildren<Button>(true);
            if (back) Anchor((RectTransform)back.transform, 0, 40);
        }

        static void AddHelpText(Transform row, string value, Text model, bool key, float width)
        {
            var text = new GameObject(key ? "按键" : "说明", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            text.transform.SetParent(row, false);
            text.font = model.font;
            text.fontSize = model.fontSize;
            text.alignment = TextAnchor.MiddleLeft;
            text.text = value;
            text.raycastTarget = false;
            text.color = model.color;
            var element = text.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.flexibleWidth = key ? 0 : 1;
            if (key) text.gameObject.AddComponent<ThemeRole>().kind = ThemeRoleKind.Title;
        }

        static void StyleErrorPanel(GameObject root, InterfaceTheme theme)
        {
            var panels = root.GetComponentInChildren<PlayPanels>(true);
            var errorPanel = panels && panels.error ? panels.error.transform.Find("面板") : null;
            var image = errorPanel ? errorPanel.GetComponent<Image>() : null;
            if (!image || !theme.panelErrorSprite) return;
            image.sprite = theme.panelErrorSprite;
            image.type = Image.Type.Sliced;
            image.color = Color.white;
            var style = root.GetComponent<InterfaceThemeScope>();
            style.panels = style.panels.Where(item => item != image).ToArray();
        }

        public static void BuildReview()
        {
            string output = Environment.GetEnvironmentVariable("SOKOBAN_UI_REVIEW_BUILD");
            if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("SOKOBAN_UI_REVIEW_BUILD 未设置");
            var result = BuildPipeline.BuildPlayer(new[] { MenuPath, "Assets/Scenes/Game.unity", EditorPath }, output,
                BuildTarget.StandaloneWindows64, BuildOptions.None).summary;
            if (result.result != UnityEditor.Build.Reporting.BuildResult.Succeeded || result.totalErrors != 0)
                throw new InvalidOperationException("Windows 构建失败：" + result.result + " / " + result.totalErrors);
            Debug.Log("WS-06V / WS-06P Windows build succeeded " + output);
        }
    }

    [CustomEditor(typeof(InterfaceTheme))]
    internal sealed class InterfaceThemeInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if (!DrawDefaultInspector()) return;
            foreach (var page in Resources.FindObjectsOfTypeAll<InterfaceThemeScope>())
                if (page.theme == (InterfaceTheme)target && page.gameObject.scene.IsValid()) page.Apply();
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }
    }
}
