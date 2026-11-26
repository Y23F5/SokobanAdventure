/**
 * @file ObjectGhost.cs
 * @brief Show 为当前画刷或所选对象建一个半透明预览，把锚点放到指定格并给全部子网格上色；外观取自 Core.AppearanceRef，状态表取初始状态。OverFreshPlacement 在指针换格前保持刚放置的模型可见。Hide 在工具离开地图时丢弃实例，Dispose 释放持有的材质。
 * 外观解析顺序：状态表初始状态的外观、对象或画刷的外观引用、所属类别按主题的默认外观；内置外观走 Prefab，本房模型资源走 ImportedAppearance。
 */
using System.Linq;
using Sokoban.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sokoban
{
    public sealed class ObjectGhost
    {
        GameObject model;
        Material tint;
        string previous;
        RoomDraft hoverDraft;
        int hoverRevision = -1;
        Cell? justPlaced;

        bool OverFreshPlacement(RoomDraft draft, Cell world)
        {
            if (draft != hoverDraft || hoverRevision != draft.Revision)
            {
                hoverDraft = draft;
                hoverRevision = draft.Revision;
                justPlaced = draft.PlacementRevision == draft.Revision && draft.SelectedCell.HasValue
                    ? draft.SelectedCell.Value + draft.Origin : (Cell?)null;
            }
            if (world != justPlaced) justPlaced = null;
            return world == justPlaced;
        }

        public void Show(RoomDraft draft, Cell world, bool moving, string error)
        {
            if (!moving && OverFreshPlacement(draft, world))
            {
                Hide();
                return;
            }
            var placed = moving && draft.SelectedCell.HasValue ? draft.Room.ObjectAt(draft.SelectedCell.Value) : null;
            string category = moving ? placed?.CategoryId : draft.BrushCategory;
            var look = moving ? placed?.Appearance : draft.BrushAppearance;
            var states = moving && placed != null ? ObjectStateProfile.StatesOf(draft.Model, placed.Id) : draft.DefinitionStates;
            if (states != null) look = new AppearanceRef(ObjectStateProfile.StateLook(states.Initial));
            look ??= AppearanceLibrary.Default(category ?? ObjectCategories.Target, draft.Theme);
            string prefab = AppearanceLibrary.Prefab(look);
            string modelId = prefab == null && draft.Model.Assets.Any(a => a.Id == look?.Id && a.Kind == AssetKinds.Model) ? look.Id : null;
            if (prefab == null && modelId == null) prefab = AppearanceLibrary.Prefab(AppearanceLibrary.Default(ObjectCategories.Target, draft.Theme));
            string key = draft.ActiveId + "|" + prefab + "|" + modelId;
            if (!model || previous != key)
            {
                Hide();
                if (!tint) tint = new Material(Resources.Load<Material>("LayoutModels/GhostSurface")) { hideFlags = HideFlags.HideAndDontSave };
                model = modelId != null ? new GameObject() : Object.Instantiate(Resources.Load<GameObject>(prefab));
                if (prefab == "LayoutModels/Wall") model.transform.localScale = new Vector3(1, 2.6f, 1);
                model.name = "对象放置预览";
                foreach (var child in model.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.HideAndDontSave;
                if (modelId != null)
                {
                    var appearance = model.AddComponent<ImportedAppearance>();
                    appearance.Apply(draft.Model.Assets, modelId);
                    appearance.SetGhost(tint);
                }
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    var slots = renderer.sharedMaterials;
                    for (int i = 0; i < slots.Length; i++) slots[i] = tint;
                    renderer.sharedMaterials = slots;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                previous = key;
            }
            var color = error == null ? Color.green : Color.red;
            color.a = .43f;
            tint.SetColor("_BaseColor", color);
            model.transform.position = new Vector3(world.X, .025f, world.Y);
        }

        public void Hide()
        {
            if (!model) return;
            model.SetActive(false);
            DestroyTransient(model);
            model = null;
        }

        public void Dispose() { Hide(); if (tint) DestroyTransient(tint); tint = null; }

        static void DestroyTransient(Object obj) { if (Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); }
    }
}
