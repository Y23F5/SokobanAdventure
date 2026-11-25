/**
 * @file InterfaceTooltip.cs
 * @brief Pointer and keyboard focus callbacks show the configured tooltip after a short dwell. Update clamps the tooltip to its parent rect.
 * Disable and pointer exit clear pending display so a hidden or replaced page cannot leave a tooltip on screen.
 */
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sokoban
{
    public sealed class InterfaceTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public RectTransform tooltip;
        public Text label;
        [TextArea] public string description;
        bool hover, focus;
        float due;

        public void OnPointerEnter(PointerEventData data) { hover = true; due = Time.unscaledTime + .45f; }
        public void OnPointerExit(PointerEventData data) { hover = false; if (!focus) Hide(); }
        public void OnSelect(BaseEventData data) { focus = true; due = Time.unscaledTime + .45f; }
        public void OnDeselect(BaseEventData data) { focus = false; if (!hover) Hide(); }
        void OnDisable() { hover = focus = false; Hide(); }
        void Hide() { if (tooltip) tooltip.gameObject.SetActive(false); }

        void Update()
        {
            if (!tooltip || !label || string.IsNullOrWhiteSpace(description) || (!hover && !focus) || Time.unscaledTime < due) return;
            if (!(tooltip.parent is RectTransform bounds)) return;
            var target = (RectTransform)transform;
            label.text = description;
            tooltip.gameObject.SetActive(true);
            Vector2 position = bounds.InverseTransformPoint(target.TransformPoint(new Vector3(target.rect.center.x, target.rect.yMin)));
            var area = bounds.rect;
            var size = tooltip.rect.size;
            position.x = Mathf.Clamp(position.x - size.x * .5f, area.xMin, Mathf.Max(area.xMin, area.xMax - size.x));
            position.y = Mathf.Clamp(position.y - 5, Mathf.Min(area.yMax, area.yMin + size.y), area.yMax);
            tooltip.position = bounds.TransformPoint(position);
        }
    }
}
