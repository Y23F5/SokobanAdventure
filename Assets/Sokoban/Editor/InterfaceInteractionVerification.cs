/**
 * @file InterfaceInteractionVerification.cs
 * @brief Run starts Play Mode from the production menu. Step drives native uGUI events on all eight template types and checks production navigation.
 * VerifyButtonArtwork renders actual scene buttons at the same coordinates and compares their backgrounds in both states.
 * CheckMenuButtons verifies that the editor entry shares a row with the other main actions and receives the same focus styling.
 * Update advances the test once per frame and exits the batch process with the observed result.
 */
using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Sokoban.Editor
{
    public static class InterfaceInteractionVerification
    {
        const string RunKey = "Sokoban.UIV002.Run";
        static IEnumerator steps;
        static int frame = -1;

        public static void Run()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/MainMenu.unity");
            SessionState.SetBool(RunKey, true);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Hook() => EditorApplication.update += Update;

        static void Update()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(RunKey, false) || frame == Time.frameCount) return;
            frame = Time.frameCount;
            int exitCode = 0;
            try { if ((steps ??= Step()).MoveNext()) return; }
            catch (Exception error) { Debug.LogException(error); exitCode = 1; }
            SessionState.EraseBool(RunKey);
            EditorApplication.Exit(exitCode);
        }

        static GameObject Spawn(string name, Transform parent)
        {
            var template = Resources.Load<GameObject>("LayoutUI/" + name);
            Require(template, name + " 模板加载");
            return Object.Instantiate(template, parent, false);
        }

        static void Require(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException("UIV-002 " + label);
            Debug.Log("UIV-002 PASS " + label);
        }

        static IEnumerator WaitForSprite(Graphic graphic, Sprite expected, string label)
        {
            var image = (Image)graphic;
            float end = Time.realtimeSinceStartup + 2;
            while (image.overrideSprite != expected && Time.realtimeSinceStartup < end)
                yield return null;
            Require(expected && image.overrideSprite == expected, label);
        }

        static IEnumerator WaitForTint(Graphic graphic, Color expected, string label)
        {
            float end = Time.realtimeSinceStartup + 2;
            while (Vector4.Distance(graphic.canvasRenderer.GetColor(), expected) >= .02f && Time.realtimeSinceStartup < end)
                yield return null;
            Require(Vector4.Distance(graphic.canvasRenderer.GetColor(), expected) < .02f, label);
        }

        static void VerifyButtonArtwork(Button[] buttons, Color baseColor, Color activeColor, string group,
            Transform menuBoard = null)
        {
            var stage = new GameObject("界面对照", typeof(RectTransform), typeof(Canvas));
            stage.layer = 30;
            var viewpoint = new GameObject("书签相机", typeof(Camera));
            var camera = viewpoint.GetComponent<Camera>();
            var target = menuBoard ? new RenderTexture(560, 510, 24) : new RenderTexture(260, 92, 24);
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color32(249, 224, 170, 255);
                camera.cullingMask = 1 << 30;
                camera.targetTexture = target;
                camera.orthographic = true;
                camera.orthographicSize = target.height * .5f;
                camera.transform.position = Vector3.back * 10;
                var canvas = stage.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                ((RectTransform)stage.transform).sizeDelta = new Vector2(target.width, target.height);
                var copies = buttons.Select(button => Object.Instantiate(button, stage.transform, false)).ToArray();
                foreach (var copy in copies)
                {
                    foreach (Transform part in copy.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 30;
                    var rect = (RectTransform)copy.transform;
                    rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
                    rect.anchoredPosition = Vector2.zero;
                    copy.GetComponentInChildren<Text>().enabled = false;
                    copy.interactable = true;
                    copy.gameObject.SetActive(false);
                }

                void Render()
                {
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                    pixels.Apply();
                }

                Color32[] normal = null;
                foreach (var state in new[] { (name: "未选中", tint: baseColor), (name: "选中", tint: activeColor) })
                {
                    Color32[] reference = null;
                    foreach (var copy in copies)
                    {
                        copy.gameObject.SetActive(true);
                        copy.image.color = state.tint;
                        Render();
                        var frame = pixels.GetPixels32();
                        if (reference == null) reference = frame;
                        else Require(frame.SequenceEqual(reference), group + " · " + copy.name + " " + state.name + "时与同组首项边框及底色逐像素一致");
                        copy.gameObject.SetActive(false);
                    }
                    Require(!reference[target.width * (target.height / 2) + target.width / 2].Equals(reference[0]) &&
                        reference.Distinct().Count() > 3, group + " · " + state.name + "时渲染得到边框和底色");
                    if (normal == null) normal = reference;
                    else Require(!normal.SequenceEqual(reference), "两个状态的渲染颜色可区分");
                }

                string output = Environment.GetEnvironmentVariable("SOKOBAN_BOOKMARK_EVIDENCE");
                if (string.IsNullOrWhiteSpace(output)) return;
                Directory.CreateDirectory(output);
                if (menuBoard)
                {
                    var view = (RectTransform)Object.Instantiate(menuBoard, stage.transform, false);
                    foreach (Transform part in view.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 30;
                    view.anchorMin = view.anchorMax = new Vector2(.5f, .5f);
                    view.anchoredPosition = Vector2.zero;
                    Render();
                    File.WriteAllBytes(Path.Combine(output, "主菜单-默认态.png"), pixels.EncodeToPNG());
                    return;
                }
                if (group != "分页标签") return;
                for (int i = 0; i < copies.Length; i++)
                {
                    var copy = copies[i];
                    copy.gameObject.SetActive(true);
                    ((RectTransform)copy.transform).anchoredPosition = new Vector2((i - 1) * 72, 0);
                    copy.GetComponentInChildren<Text>().enabled = true;
                }
                for (int selected = 0; selected < copies.Length; selected++)
                {
                    for (int i = 0; i < copies.Length; i++) copies[i].image.color = i == selected ? activeColor : baseColor;
                    Render();
                    File.WriteAllBytes(Path.Combine(output, $"书签-{selected + 1}.png"), pixels.EncodeToPNG());
                }
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.Destroy(pixels);
                Object.Destroy(target);
                Object.Destroy(stage);
                Object.Destroy(viewpoint);
            }
        }

        static IEnumerator CheckMenuButtons(MainMenuNavigation menu, EventSystem events)
        {
            var board = menu.continueButton.transform.parent;
            var buttons = board.GetComponentsInChildren<Button>(true);
            string Action(Button button) => button.onClick.GetPersistentMethodName(0);
            var primary = new[] { "StartGame", "ContinueGame", "OpenEditor", "Quit" }
                .Select(action => buttons.Single(button => Action(button) == action)).ToArray();
            var secondary = new[] { "Settings", "Help" }
                .Select(action => buttons.Single(button => Action(button) == action)).ToArray();
            Require(buttons.Length == 6 && buttons.All(button => button.transform.parent == board), "主菜单六个入口同级，无独立背景包裹");
            Require(primary.Select(button => ((RectTransform)button.transform).rect.size).Distinct().Count() == 1 &&
                secondary.Select(button => ((RectTransform)button.transform).rect.size).Distinct().Count() == 1 &&
                buttons.Select(button => button.GetComponentInChildren<Text>().fontSize).Distinct().Count() == 1,
                "四个主入口等宽等高，设置与操作说明等宽等高，六项字号相同");
            Require(events.currentSelectedGameObject == primary[0].gameObject, "启动时键盘焦点在开始游戏");
            var theme = board.GetComponentInParent<InterfaceThemeScope>().theme;
            var editor = primary[2];
            events.SetSelectedGameObject(null);
            var normal = WaitForTint(editor.targetGraphic, Color.white, "关卡编辑器失去焦点时恢复默认底色");
            while (normal.MoveNext()) yield return null;
            VerifyButtonArtwork(primary, theme.parchment, theme.parchment * theme.focus, "主菜单主入口", board);
            VerifyButtonArtwork(secondary, theme.parchment, theme.parchment * theme.focus, "主菜单并排入口");
            events.SetSelectedGameObject(editor.gameObject);
            var focus = WaitForSprite(editor.targetGraphic, theme.buttonHoverSprite, "关卡编辑器获得焦点时显示选中精灵");
            while (focus.MoveNext()) yield return null;
            events.SetSelectedGameObject(primary[0].gameObject);
            normal = WaitForTint(editor.targetGraphic, Color.white, "焦点移回开始游戏后关卡编辑器再次恢复默认底色");
            while (normal.MoveNext()) yield return null;
        }

        static IEnumerator Step()
        {
            yield return null;
            yield return null;
            var menu = Object.FindObjectOfType<MainMenuNavigation>();
            var panels = menu.panels;
            var events = EventSystem.current;
            Require(events && events.currentSelectedGameObject && events.currentSelectedGameObject.GetComponent<Button>().interactable, "主菜单初始键盘焦点");
            var menuChecks = CheckMenuButtons(menu, events);
            while (menuChecks.MoveNext()) yield return null;
            panels.Settings();
            Require(panels.settings.activeInHierarchy && panels.Modal && events.currentSelectedGameObject.transform.IsChildOf(panels.settings.transform), "设置与焦点");
            panels.Back();
            Require(!panels.settings.activeSelf && events.currentSelectedGameObject, "设置返回主菜单");

            var canvas = menu.continueButton.GetComponentInParent<Canvas>().transform;
            var button = Spawn("Button", canvas).GetComponent<Button>();
            var theme = button.GetComponent<InterfaceThemeScope>().theme;
            var pointer = new PointerEventData(events) { button = PointerEventData.InputButton.Left };
            int clicks = 0;
            button.onClick.AddListener(() => clicks++);
            var states = WaitForTint(button.targetGraphic, Color.white, "按钮默认");
            while (states.MoveNext()) yield return null;
            button.OnPointerEnter(pointer);
            states = WaitForSprite(button.targetGraphic, theme.buttonHoverSprite, "按钮悬停");
            while (states.MoveNext()) yield return null;
            button.OnPointerDown(pointer);
            states = WaitForSprite(button.targetGraphic, theme.buttonPressedSprite, "按钮按下");
            while (states.MoveNext()) yield return null;
            button.OnPointerUp(pointer);
            button.OnPointerClick(pointer);
            button.OnPointerExit(pointer);
            states = WaitForSprite(button.targetGraphic, theme.buttonHoverSprite, "按钮焦点和选中");
            while (states.MoveNext()) yield return null;
            button.interactable = false;
            button.OnPointerClick(pointer);
            states = WaitForSprite(button.targetGraphic, theme.buttonDisabledSprite, "按钮禁用视觉");
            while (states.MoveNext()) yield return null;
            Require(clicks == 1, "禁用后不响应点击");

            var input = Spawn("Input", canvas).GetComponent<InputField>();
            input.Select();
            input.ActivateInputField();
            yield return null;
            input.text = "房间布局";
            Require(input.isFocused && input.textComponent.text == "房间布局", "输入框焦点与中文内容");

            var dropdown = Spawn("Dropdown", canvas).GetComponent<Dropdown>();
            yield return null;
            dropdown.Show();
            var list = dropdown.transform.Find("Dropdown List");
            Require(list && list.GetComponentsInChildren<Toggle>().Length > 1, "下拉展开选项");
            list.GetComponentsInChildren<Toggle>().Last().isOn = true;
            Require(dropdown.value == dropdown.options.Count - 1, "下拉选择并更新值");

            var bookmark = Spawn("Bookmark", canvas).GetComponent<Button>();
            bookmark.Select();
            Require(events.currentSelectedGameObject == bookmark.gameObject, "分页标签可聚焦");
            var card = Spawn("MaterialCard", canvas).GetComponent<Button>();
            Require(card.GetComponentInChildren<Text>() && card.GetComponentInChildren<Image>(), "素材卡包含文字与图像");
            var scroll = Spawn("ScrollList", canvas).GetComponent<ScrollRect>();
            scroll.content.sizeDelta = new Vector2(0, 640);
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = 1;
            float top = scroll.content.anchoredPosition.y;
            scroll.verticalNormalizedPosition = 0;
            Require(Mathf.Abs(scroll.content.anchoredPosition.y - top) > 10, "滚动列表到达底部");

            var dialog = Spawn("Dialog", canvas);
            var close = dialog.GetComponentInChildren<Button>();
            close.OnPointerClick(pointer);
            Require(!dialog.activeSelf, "弹窗模板关闭");
            var tip = Spawn("Tooltip", canvas);
            Require(!tip.GetComponent<Image>().raycastTarget, "提示不截取地图鼠标");

            menu.continueButton.transform.parent.GetComponentsInChildren<Button>().Single(entry =>
                entry.onClick.GetPersistentMethodName(0) == "OpenEditor").onClick.Invoke();
            float openingDeadline = Time.realtimeSinceStartup + 10;
            while (!Object.FindObjectOfType<LayoutHost>() && Time.realtimeSinceStartup < openingDeadline) yield return null;
            Require(Object.FindObjectOfType<LayoutHost>(), "主菜单关卡编辑器按钮进入真实编辑场景");
            yield return null;
            var layout = Object.FindObjectOfType<RuntimeLayoutUI>();
            var tabs = new[] { layout.objectTab, layout.terrainTab, layout.assetTab };
            var sizes = tabs.Select(tab => ((RectTransform)tab.transform).sizeDelta).Distinct();
            Require(sizes.Count() == 1 && tabs.Select(tab => tab.image.sprite).Distinct().Count() == 1 &&
                tabs.Select(tab => tab.colors).Distinct().Count() == 1 && tabs.Select(tab => tab.GetComponentInChildren<Text>().fontSize).Distinct().Count() == 1,
                "物件、地形、自定义书签共用尺寸、边框、状态颜色和字号");
            var themeForTabs = layout.interfaceStyle.theme;
            for (int activeTab = 0; activeTab < tabs.Length; activeTab++)
            {
                var marks = layout.Bookmarks;
                marks[activeTab].onClick.Invoke();
                yield return null;
                Require(marks.Select((tab, index) => tab.image.sprite == (index == activeTab ? themeForTabs.bookmarkSelectedSprite : themeForTabs.bookmarkSprite) &&
                    tab.transform.localRotation == Quaternion.identity).All(value => value), tabs[activeTab].name + "选中后只改变高亮");
            }
            VerifyButtonArtwork(tabs, themeForTabs.parchment, themeForTabs.active, "分页标签");
            var toolbar = Object.FindObjectOfType<InterfaceTooltip>();
            Require(toolbar && !toolbar.tooltip.gameObject.activeSelf, "真实编辑器的工具提示初始隐藏");
            toolbar.OnPointerEnter(new PointerEventData(EventSystem.current));
            float due = Time.realtimeSinceStartup + 2;
            while (!toolbar.tooltip.gameObject.activeSelf && Time.realtimeSinceStartup < due) yield return null;
            Require(toolbar.tooltip.gameObject.activeSelf && toolbar.label.text == toolbar.description, "编辑工具提示出现");
            toolbar.OnPointerExit(new PointerEventData(EventSystem.current));
            Require(!toolbar.tooltip.gameObject.activeSelf, "离开编辑工具时关闭提示");
            var editor = Object.FindObjectOfType<LayoutHost>();
            editor.Panel.Draft.NewDefaultRoom();
            editor.Panel.Draft.Place(true, new Sokoban.Core.Cell(3, 2));
            editor.Panel.Draft.Place(false, new Sokoban.Core.Cell(4, 2));
            VerificationFixtures.EnsureRecovery(editor.Panel.Draft);
            editor.StartTrial();
            yield return null;
            yield return null;
            var host = Object.FindObjectOfType<PuzzleHost>();
            Require(host && host.IsTrial, "真实编辑器进入试玩");
            host.panels.Pause();
            host.panels.Settings();
            host.panels.Back();
            Require(host.panels.pause.activeSelf && !host.panels.settings.activeSelf, "设置返回暂停页");
            host.panels.Back();
            Require(host.panels.hud.activeSelf && !host.panels.pause.activeSelf && !EventSystem.current.currentSelectedGameObject, "继续游玩恢复 HUD 与地图输入");
            host.Leave();
            yield return null;
            yield return null;
            Require(Object.FindObjectOfType<LayoutHost>(), "结束试玩回到编辑器");
        }
    }
}
