/**
 * @file InterfaceThemeScope.cs
 * @brief OnEnable and Update refresh the native Unity page when its theme changes. Capture records authored text sizing and layout spacing for repeatable live previews.
 * Apply updates typography and linked controls, retaining per-page typography and spacing offsets. Reusable prefab roots can have their own scope.
 */
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban
{
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class InterfaceThemeScope : MonoBehaviour
    {
        [Serializable] struct Label
        {
            public Text text;
            public int size, min, max;
            public Vector2 insetMin, insetMax;
            public bool stretched;
            public bool isTitle;
        }
        [Serializable] struct Spacing { public HorizontalOrVerticalLayoutGroup group; public float value; }
        const int TitleFontSize = 24;
        const string PanelShadowName = "面板阴影";
        public InterfaceTheme theme;
        public Image[] panels = Array.Empty<Image>();
        [SerializeField] List<Label> labels = new List<Label>();
        [SerializeField] List<Spacing> layout = new List<Spacing>();
        InterfaceTheme applied;
        int revision = -1;

        void OnEnable() => Apply();
        void Update()
        {
            if (theme != applied || theme && theme.Revision != revision) Apply();
        }

        public void Capture()
        {
            labels.Clear();
            foreach (var text in GetComponentsInChildren<Text>(true))
                CaptureLabel(text);
            layout.Clear();
            foreach (var stack in GetComponentsInChildren<HorizontalOrVerticalLayoutGroup>(true))
                layout.Add(new Spacing { group = stack, value = stack.spacing });
        }

        void CaptureLabel(Text text)
        {
            var rect = text.rectTransform;
            labels.Add(new Label
            {
                text = text, size = text.fontSize, min = text.resizeTextMinSize, max = text.resizeTextMaxSize,
                insetMin = rect.offsetMin, insetMax = rect.offsetMax,
                stretched = rect.anchorMax.x > rect.anchorMin.x && (!rect.parent || !rect.parent.GetComponent<LayoutGroup>()),
                isTitle = text.fontSize >= TitleFontSize
            });
        }

        public void Apply()
        {
            applied = theme;
            if (!theme) return;
            revision = theme.Revision;
            foreach (var entry in labels)
            {
                if (!entry.text) continue;
                var face = entry.isTitle && theme.titleFont ? theme.titleFont : theme.font;
                if (face) entry.text.font = face;
                entry.text.color = theme.ink;
                entry.text.fontSize = Mathf.Max(1, entry.size + theme.fontSizeAdjustment);
                entry.text.resizeTextMinSize = Mathf.Max(1, entry.min + theme.fontSizeAdjustment);
                entry.text.resizeTextMaxSize = Mathf.Max(entry.text.resizeTextMinSize, entry.max + theme.fontSizeAdjustment);
                if (entry.stretched)
                {
                    var inset = new Vector2(theme.spacingAdjustment, 0);
                    entry.text.rectTransform.offsetMin = entry.insetMin + inset;
                    entry.text.rectTransform.offsetMax = entry.insetMax - inset;
                }
            }
            foreach (var entry in layout)
                if (entry.group) entry.group.spacing = entry.value + theme.spacingAdjustment;
            foreach (var image in panels)
            {
                if (!image) continue;
                ApplySurface(image, theme.panel, theme.panelSprite);
                if (theme.panelSprite && image.transform.parent && image.transform.parent.name == PanelShadowName)
                {
                    var shadow = image.transform.parent.GetComponent<Image>();
                    if (shadow && !shadow.sprite) shadow.enabled = false;
                }
            }
            foreach (var control in GetComponentsInChildren<Selectable>(true))
            {
                if (control.transition == Selectable.Transition.ColorTint) control.colors = theme.Controls;
                if (control is Slider slider)
                {
                    ApplySlider(slider);
                    continue;
                }
                if (control is Toggle toggle)
                {
                    ApplyToggle(toggle);
                    continue;
                }
                var image = control.targetGraphic as Image;
                if (!image) continue;
                if (control is Button || control is Dropdown) ApplySurface(image, theme.parchment, ButtonSprite(control));
                else if (control is InputField)
                {
                    ApplySurface(image, theme.field, theme.inputSprite);
                    var field = (InputField)control;
                    field.customCaretColor = true;
                    field.caretColor = theme.ink;
                    field.selectionColor = theme.active;
                }
            }
        }

        static bool InPage(Selectable control, GameObject page) => page && control.transform.IsChildOf(page.transform);

        Sprite ButtonSprite(Selectable control)
        {
            if (theme.bookmarkSprite && control.name != null && (control.name == "Bookmark" || control.name == "物件" || control.name == "地形" || control.name == "自定义")) return theme.bookmarkSprite;
            var panels = GetComponentInParent<PlayPanels>(true);
            bool inPlayPage = panels && (InPage(control, panels.pause) || InPage(control, panels.settings) || InPage(control, panels.confirm) || InPage(control, panels.error));
            if (!inPlayPage) return theme.buttonSprite;
            var label = control.GetComponentInChildren<Text>(true);
            string text = label ? label.text : null;
            if (!string.IsNullOrEmpty(text))
            {
                if (text.Contains("退出") && theme.buttonDangerSprite) return theme.buttonDangerSprite;
                if ((text == "继续游玩" || text.Contains("保存并返回") || text.Contains("覆盖并开始")) && theme.buttonPrimarySprite) return theme.buttonPrimarySprite;
            }
            return theme.buttonSprite;
        }

        void ApplySlider(Slider slider)
        {
            var knob = slider.targetGraphic as Image;
            if (knob && theme.sliderKnobSprite) ApplySurface(knob, Color.white, theme.sliderKnobSprite);
            if (!theme.sliderTrackSprite) return;
            var track = slider.transform.Find("Background");
            var trackImage = track ? track.GetComponent<Image>() : null;
            if (trackImage)
            {
                ApplySurface(trackImage, Color.white, theme.sliderTrackSprite);
                var rect = trackImage.rectTransform;
                rect.anchorMin = new Vector2(rect.anchorMin.x, 0);
                rect.anchorMax = new Vector2(rect.anchorMax.x, 1);
            }
            var fillArea = slider.transform.Find("Fill Area") as RectTransform;
            if (fillArea)
            {
                fillArea.anchorMin = new Vector2(fillArea.anchorMin.x, 0);
                fillArea.anchorMax = new Vector2(fillArea.anchorMax.x, 1);
            }
            var fill = slider.fillRect ? slider.fillRect.GetComponent<Image>() : null;
            if (fill) ApplySurface(fill, Color.white, theme.sliderTrackSprite);
        }

        void ApplyToggle(Toggle toggle)
        {
            var box = toggle.targetGraphic as Image;
            if (box && theme.checkOffSprite) ApplySurface(box, Color.white, theme.checkOffSprite);
            var mark = toggle.graphic as Image;
            if (mark && theme.checkOnSprite)
            {
                mark.sprite = theme.checkOnSprite;
                mark.type = Image.Type.Sliced;
                mark.color = Color.white;
            }
        }

        void ApplySurface(Image image, Color tint, Sprite sprite)
        {
            if (sprite)
            {
                image.color = Color.white;
                image.sprite = sprite;
                image.type = Image.Type.Sliced;
                return;
            }
            image.color = tint;
            if (!theme.controlFrame) return;
            image.sprite = theme.controlFrame;
            image.type = Image.Type.Sliced;
        }
    }
}
