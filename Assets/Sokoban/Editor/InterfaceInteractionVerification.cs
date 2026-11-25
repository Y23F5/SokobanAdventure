/**
 * @file InterfaceInteractionVerification.cs
 * @brief Run starts Play Mode from the production menu. Step drives native uGUI events on all eight template types and checks production navigation.
 * Update advances the test once per frame and exits the batch process with the observed result.
 */
using System;
using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
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

        static IEnumerator WaitForTint(Graphic graphic, Color expected, string label)
        {
            float end = Time.realtimeSinceStartup + 2;
            while (Vector4.Distance(graphic.canvasRenderer.GetColor(), expected) >= .02f && Time.realtimeSinceStartup < end)
                yield return null;
            Require(Vector4.Distance(graphic.canvasRenderer.GetColor(), expected) < .02f, label);
        }

        static IEnumerator Step()
        {
            yield return null;
            yield return null;
            var menu = Object.FindObjectOfType<MainMenuNavigation>();
            var panels = menu.panels;
            var events = EventSystem.current;
            Require(events && events.currentSelectedGameObject && events.currentSelectedGameObject.GetComponent<Button>().interactable, "主菜单初始键盘焦点");
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
            states = WaitForTint(button.targetGraphic, theme.hover, "按钮悬停");
            while (states.MoveNext()) yield return null;
            button.OnPointerDown(pointer);
            states = WaitForTint(button.targetGraphic, theme.pressed, "按钮按下");
            while (states.MoveNext()) yield return null;
            button.OnPointerUp(pointer);
            button.OnPointerClick(pointer);
            button.OnPointerExit(pointer);
            states = WaitForTint(button.targetGraphic, theme.focus, "按钮焦点和选中");
            while (states.MoveNext()) yield return null;
            button.interactable = false;
            button.OnPointerClick(pointer);
            states = WaitForTint(button.targetGraphic, theme.disabled, "按钮禁用视觉");
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

            SceneManager.LoadScene("RuntimeEditor");
            yield return null;
            yield return null;
            var layout = Object.FindObjectOfType<RuntimeLayoutUI>();
            var tabs = new[] { layout.objectTab, layout.terrainTab, layout.assetTab };
            var sizes = tabs.Select(tab => ((RectTransform)tab.transform).sizeDelta).Distinct();
            Require(sizes.Count() == 1 && tabs.Select(tab => tab.image.sprite).Distinct().Count() == 1 &&
                tabs.Select(tab => tab.colors).Distinct().Count() == 1 && tabs.Select(tab => tab.GetComponentInChildren<Text>().fontSize).Distinct().Count() == 1,
                "物件、地形、自定义书签共用尺寸、边框、状态颜色和字号");
            layout.Act("选素材");
            yield return null;
            Require(layout.assetTab.image.color == layout.interfaceStyle.theme.active && tabs.All(tab => tab.transform.localRotation == Quaternion.identity),
                "切换自定义书签只高亮，不改变布局姿态");
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
