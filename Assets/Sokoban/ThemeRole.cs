/**
 * @file ThemeRole.cs
 * @brief ThemeRoleKind 列出界面元素的外观角色。ThemeRole 挂在文字、按钮或书签上，显式声明角色，InterfaceThemeScope 据此选择字重与精灵。
 * OnPointerDown、OnPointerUp、OnPointerExit 在按钮按下时把标签下移 2px，松开或移出时复位。
 * SetSelected 切换书签选中态：换选中精灵、整体上抬 6px、字重 900，取消时复位。Selected 供 InterfaceThemeScope 重新套用主题时保持选中外观。
 */
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sokoban
{
    public enum ThemeRoleKind { Body, Title, Primary, Danger, Bookmark, Card }

    [DisallowMultipleComponent]
    public sealed class ThemeRole : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public ThemeRoleKind kind;
        const float PressDrop = 2;
        const float BookmarkLift = 6;
        Text label;
        Vector2 labelHome;
        bool dropped;
        bool lifted;

        public bool Selected => lifted;

        void OnDisable() => Release();

        public void OnPointerDown(PointerEventData data)
        {
            var control = GetComponent<Selectable>();
            if (dropped || kind == ThemeRoleKind.Bookmark || kind == ThemeRoleKind.Title || !control || !control.IsInteractable()) return;
            label = GetComponentInChildren<Text>(true);
            if (!label) return;
            labelHome = label.rectTransform.anchoredPosition;
            label.rectTransform.anchoredPosition = labelHome + new Vector2(0, -PressDrop);
            dropped = true;
        }

        public void OnPointerUp(PointerEventData data) => Release();

        public void OnPointerExit(PointerEventData data) => Release();

        void Release()
        {
            if (!dropped) return;
            dropped = false;
            if (label) label.rectTransform.anchoredPosition = labelHome;
        }

        public void SetSelected(bool on)
        {
            if (kind != ThemeRoleKind.Bookmark || on == lifted) return;
            lifted = on;
            float lift = on ? BookmarkLift : -BookmarkLift;
            var rect = (RectTransform)transform;
            rect.sizeDelta += new Vector2(0, lift);
            rect.anchoredPosition += new Vector2(0, lift * rect.pivot.y);
            var scope = GetComponentInParent<InterfaceThemeScope>(true);
            var theme = scope ? scope.theme : null;
            if (!theme) return;
            var image = GetComponent<Image>();
            var sprite = on ? theme.bookmarkSelectedSprite : theme.bookmarkSprite;
            if (image && sprite) image.sprite = sprite;
            var text = GetComponentInChildren<Text>(true);
            var face = on && theme.titleFont ? theme.titleFont : theme.font;
            if (text && face) text.font = face;
        }
    }
}
