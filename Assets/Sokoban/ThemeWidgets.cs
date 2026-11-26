/**
 * @file ThemeWidgets.cs
 * @brief 运行时界面部件工厂，精灵与颜色全部取自 InterfaceTheme，让代码动态创建的控件与预制件外观一致。
 * Tag 创建已完成或未完成标签；WarnLine 创建带图标的红色警告条；Thumb 创建缩略图底框；DashedCard 创建虚线占位卡并返回其按钮。
 * SmallPanel 创建左上悬浮的小面板；ErrorPanel 创建红色描边的错误面板；WarningIcon 创建 64px 警告图标。
 * Theme 首次访问时从 Resources 读取 LayoutUI/WarmPixel 并缓存；文字字体经 InterfaceThemeScope.WithFallback 回退。
 */
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban
{
    public static class ThemeWidgets
    {
        static readonly Color OkText = new Color32(63, 138, 78, 255);
        static readonly Color BadText = new Color32(168, 64, 43, 255);
        static readonly Color Muted = new Color32(139, 106, 72, 255);
        static InterfaceTheme cached;

        static InterfaceTheme Theme
        {
            get
            {
                if (!cached) cached = Resources.Load<InterfaceTheme>("LayoutUI/WarmPixel");
                return cached;
            }
        }

        public static RectTransform Tag(Transform parent, bool ok, string text)
        {
            var rect = Surface(parent, ok ? "标签已完成" : "标签未完成", ok ? Theme.tagOkSprite : Theme.tagBadSprite);
            var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(10, 10, 2, 2);
            row.childAlignment = TextAnchor.MiddleCenter;
            row.childForceExpandWidth = row.childForceExpandHeight = false;
            rect.gameObject.AddComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            rect.gameObject.AddComponent<LayoutElement>().minHeight = 24;
            Label(rect, text, 13, TextAnchor.MiddleCenter, ok ? OkText : BadText);
            return rect;
        }

        public static RectTransform WarnLine(Transform parent, string text)
        {
            var rect = Surface(parent, "警告条", Theme.warnLineSprite);
            var row = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            row.padding = new RectOffset(12, 12, 8, 8);
            row.spacing = 8;
            row.childAlignment = TextAnchor.MiddleLeft;
            row.childForceExpandWidth = false;
            var icon = WarningIcon(rect).GetComponent<LayoutElement>();
            icon.preferredWidth = icon.preferredHeight = 20;
            Label(rect, text, 14, TextAnchor.MiddleLeft, BadText);
            return rect;
        }

        public static RectTransform Thumb(Transform parent, float size)
        {
            var rect = Surface(parent, "缩略图", Theme.thumbSprite);
            rect.GetComponent<Image>().color = Theme.parchment;
            rect.sizeDelta = new Vector2(size, size);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.preferredHeight = size;
            return rect;
        }

        public static Button DashedCard(Transform parent, string text)
        {
            var rect = Surface(parent, "虚线卡", Theme.cardDashedSprite);
            var image = rect.GetComponent<Image>();
            image.raycastTarget = true;
            rect.gameObject.AddComponent<ThemeRole>().kind = ThemeRoleKind.Card;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = Theme.Controls;
            rect.gameObject.AddComponent<LayoutElement>().minHeight = 48;
            Label(rect, text, 15, TextAnchor.MiddleCenter, Muted);
            return button;
        }

        public static RectTransform SmallPanel(Transform parent)
        {
            var rect = Surface(parent, "小面板", Theme.panelSmallSprite);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(20, -20);
            Stack(rect, new RectOffset(24, 24, 10, 10), 0);
            return rect;
        }

        public static RectTransform ErrorPanel(Transform parent)
        {
            var rect = Surface(parent, "错误面板", Theme.panelErrorSprite);
            rect.GetComponent<Image>().raycastTarget = true;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            Stack(rect, new RectOffset(28, 28, 24, 24), 10);
            rect.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            rect.sizeDelta = new Vector2(420, 0);
            return rect;
        }

        public static RectTransform WarningIcon(Transform parent)
        {
            var rect = Surface(parent, "警告图标", Theme.warningIcon);
            var image = rect.GetComponent<Image>();
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            rect.sizeDelta = new Vector2(64, 64);
            var element = rect.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = element.preferredHeight = 64;
            return rect;
        }

        static RectTransform Surface(Transform parent, string name, Sprite sprite)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.color = sprite ? Color.white : Theme.panel;
            image.raycastTarget = false;
            return rect;
        }

        static void Stack(RectTransform rect, RectOffset padding, float spacing)
        {
            var column = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            column.padding = padding;
            column.spacing = spacing;
            column.childAlignment = TextAnchor.UpperCenter;
            column.childForceExpandWidth = column.childForceExpandHeight = false;
            var fitter = rect.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        static void Label(RectTransform parent, string text, int size, TextAnchor align, Color color)
        {
            var go = new GameObject("文字", typeof(RectTransform), typeof(LayoutText));
            var label = go.GetComponent<LayoutText>();
            go.transform.SetParent(parent, false);
            label.text = text;
            label.fontSize = size;
            label.alignment = align;
            label.color = color;
            label.raycastTarget = false;
            label.font = InterfaceThemeScope.WithFallback(Theme.font);
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
