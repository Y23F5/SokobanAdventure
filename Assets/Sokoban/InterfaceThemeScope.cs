/**
 * @file InterfaceThemeScope.cs
 * @brief OnEnable 与 Update 在主题变化时刷新原生 Unity 页面。Capture 记录文字字号、布局间距，并为按钮补上 ThemeRole，供可重复预览。
 * Apply 按 ThemeRole 选择标题字重与按钮、面板精灵：Title 用 titleFont，Primary、Danger、Bookmark、Card 选对应精灵，其余按钮用默认精灵。
 * 默认角色的按钮用 SpriteSwap 接 btn-hover、btn-pressed、btn-disabled，其余角色保留色调过渡。WithFallback 提供字体缺失时的 Noto Sans SC 与微软雅黑回退。
 * 滑条套用细轨窄滑块，勾选框套用纯色精灵。可复用的预制件根节点可以各自挂一份 Scope。
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
        }
        [Serializable] struct Spacing { public HorizontalOrVerticalLayoutGroup group; public float value; }
        const string PanelShadowName = "面板阴影";
        const float SliderTrackHeight = 14;
        static readonly Vector2 SliderKnobSize = new Vector2(14, 26);
        static Font fallbackFont;
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

        public static Font WithFallback(Font preferred)
        {
            if (preferred) return preferred;
            if (!fallbackFont)
            {
                fallbackFont = Font.CreateDynamicFontFromOSFont(new[] { "Noto Sans SC", "Microsoft YaHei UI", "Microsoft YaHei" }, 18);
                fallbackFont.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            }
            return fallbackFont;
        }

        public void Capture()
        {
            labels.Clear();
            foreach (var text in GetComponentsInChildren<Text>(true))
                CaptureLabel(text);
            layout.Clear();
            foreach (var stack in GetComponentsInChildren<HorizontalOrVerticalLayoutGroup>(true))
                layout.Add(new Spacing { group = stack, value = stack.spacing });
            foreach (var button in GetComponentsInChildren<Button>(true))
                if (!button.GetComponent<ThemeRole>()) button.gameObject.AddComponent<ThemeRole>();
        }

        void CaptureLabel(Text text)
        {
            var rect = text.rectTransform;
            labels.Add(new Label
            {
                text = text, size = text.fontSize, min = text.resizeTextMinSize, max = text.resizeTextMaxSize,
                insetMin = rect.offsetMin, insetMax = rect.offsetMax,
                stretched = rect.anchorMax.x > rect.anchorMin.x && (!rect.parent || !rect.parent.GetComponent<LayoutGroup>())
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
                var role = entry.text.GetComponentInParent<ThemeRole>(true);
                bool heavy = role && (role.kind == ThemeRoleKind.Title || role.kind == ThemeRoleKind.Bookmark && role.Selected);
                entry.text.font = WithFallback(heavy && theme.titleFont ? theme.titleFont : theme.font);
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
                ApplySurface(image, theme.panel, PanelSprite(image));
                if (theme.panelSprite && image.transform.parent && image.transform.parent.name == PanelShadowName)
                {
                    var shadow = image.transform.parent.GetComponent<Image>();
                    if (shadow && !shadow.sprite) shadow.enabled = false;
                }
            }
            foreach (var control in GetComponentsInChildren<Selectable>(true))
            {
                control.colors = theme.Controls;
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
                var role = control.GetComponent<ThemeRole>();
                if (control is Button || control is Dropdown) ApplySurface(image, theme.parchment, ButtonSprite(role));
                if (control is Button button) ApplyTransition(button, role ? role.kind : ThemeRoleKind.Body);
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

        Sprite PanelSprite(Image image)
        {
            var role = image.GetComponent<ThemeRole>();
            if (role && role.kind == ThemeRoleKind.Danger && theme.panelErrorSprite) return theme.panelErrorSprite;
            if (role && role.kind == ThemeRoleKind.Card && theme.panelSmallSprite) return theme.panelSmallSprite;
            return theme.panelSprite;
        }

        Sprite ButtonSprite(ThemeRole role)
        {
            switch (role ? role.kind : ThemeRoleKind.Body)
            {
                case ThemeRoleKind.Bookmark:
                    var bookmark = role.Selected && theme.bookmarkSelectedSprite ? theme.bookmarkSelectedSprite : theme.bookmarkSprite;
                    return bookmark ? bookmark : theme.buttonSprite;
                case ThemeRoleKind.Primary: return theme.buttonPrimarySprite ? theme.buttonPrimarySprite : theme.buttonSprite;
                case ThemeRoleKind.Danger: return theme.buttonDangerSprite ? theme.buttonDangerSprite : theme.buttonSprite;
                case ThemeRoleKind.Card: return theme.cardSprite ? theme.cardSprite : theme.buttonSprite;
                default: return theme.buttonSprite;
            }
        }

        void ApplyTransition(Button button, ThemeRoleKind kind)
        {
            bool swap = kind == ThemeRoleKind.Body && theme.buttonHoverSprite && theme.buttonPressedSprite && theme.buttonDisabledSprite;
            if (!swap)
            {
                button.transition = Selectable.Transition.ColorTint;
                return;
            }
            button.transition = Selectable.Transition.SpriteSwap;
            button.spriteState = new SpriteState
            {
                highlightedSprite = theme.buttonHoverSprite, pressedSprite = theme.buttonPressedSprite,
                selectedSprite = theme.buttonHoverSprite, disabledSprite = theme.buttonDisabledSprite
            };
        }

        void ApplySlider(Slider slider)
        {
            var knob = slider.targetGraphic as Image;
            if (knob && theme.sliderKnobSprite)
            {
                ApplySurface(knob, Color.white, theme.sliderKnobSprite);
                knob.rectTransform.sizeDelta = SliderKnobSize;
            }
            if (!theme.sliderTrackSprite) return;
            var track = slider.transform.Find("Background");
            var trackImage = track ? track.GetComponent<Image>() : null;
            if (trackImage)
            {
                ApplySurface(trackImage, Color.white, theme.sliderTrackSprite);
                CenterBand(trackImage.rectTransform);
            }
            var fillArea = slider.transform.Find("Fill Area") as RectTransform;
            if (fillArea) CenterBand(fillArea);
            var fill = slider.fillRect ? slider.fillRect.GetComponent<Image>() : null;
            if (fill) fill.enabled = false;
        }

        static void CenterBand(RectTransform rect)
        {
            rect.anchorMin = new Vector2(rect.anchorMin.x, .5f);
            rect.anchorMax = new Vector2(rect.anchorMax.x, .5f);
            rect.offsetMin = new Vector2(rect.offsetMin.x, -SliderTrackHeight / 2);
            rect.offsetMax = new Vector2(rect.offsetMax.x, SliderTrackHeight / 2);
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
