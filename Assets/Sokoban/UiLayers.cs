/**
 * @file UiLayers.cs
 * @brief 运行编辑器下拉层和模态层的 Canvas 顺序，以及地图输入的界面遮挡判断。
 * SetOrder 保留原有布局节点并为其配置独立排序与射线。PointerOverUi 查询界面和可见提示矩形。
 */
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sokoban
{
    internal sealed class UiLayers
    {
        internal const int Persistent = 5;
        internal const int Dropdown = 10;
        internal const int Modal = 20;
        internal const int Tooltip = 30;
        readonly GameObject owner;
        readonly List<RaycastResult> hits = new List<RaycastResult>();
        readonly List<InterfaceTooltip> tips = new List<InterfaceTooltip>();

        internal UiLayers(GameObject owner) { this.owner = owner; }

        internal static void SetOrder(GameObject root, int order)
        {
            var canvas = root.GetComponent<Canvas>();
            if (!canvas) canvas = root.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = order;
            if (!root.TryGetComponent<GraphicRaycaster>(out _)) root.AddComponent<GraphicRaycaster>();
        }

        internal static void SetTooltipOrder(GameObject root)
        {
            SetOrder(root, Tooltip);
            if (root.TryGetComponent<GraphicRaycaster>(out var raycaster)) Object.Destroy(raycaster);
            foreach (var graphic in root.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        }

        internal bool PointerOverUi(Vector3 position)
        {
            if (!EventSystem.current) return false;
            hits.Clear();
            var data = new PointerEventData(EventSystem.current) { position = position };
            EventSystem.current.RaycastAll(data,hits);
            if (hits.Count != 0) return true;
            owner.GetComponentsInChildren(false, tips);
            foreach (var tip in tips)
            {
                if (!tip.tooltip || !tip.tooltip.gameObject.activeInHierarchy) continue;
                var canvas = tip.tooltip.GetComponentInParent<Canvas>();
                var camera = canvas && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                if (RectTransformUtility.RectangleContainsScreenPoint(tip.tooltip, position, camera)) return true;
            }
            return false;
        }

    }
}

