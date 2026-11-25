/**
 * @file ObjectGhost.cs
 * @brief Show builds one transient model for the active brush or selected object, places its anchor at the requested grid cell, and colors all submeshes. OverFreshPlacement keeps the newly committed model visible until the pointer changes cells. Hide drops the instance when the tool leaves the map. Dispose releases the owned material.
 */
using Newtonsoft.Json.Linq;
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
        Vector2Int? justPlaced;

        bool OverFreshPlacement(RoomDraft draft, Vector2Int world)
        {
            if (draft != hoverDraft || hoverRevision != draft.Revision)
            {
                hoverDraft = draft;
                hoverRevision = draft.Revision;
                justPlaced = draft.PlacementRevision == draft.Revision && draft.SelectedCell.HasValue
                    ? draft.SelectedCell.Value + draft.Origin : (Vector2Int?)null;
            }
            if (world != justPlaced) justPlaced = null;
            return world == justPlaced;
        }
        public void Show(RoomDraft draft, Vector2Int world, bool moving, string error)
        {
            if (!moving && OverFreshPlacement(draft, world))
            {
                Hide();
                return;
            }
            var obj = moving ? draft.Find(draft.Selected) : null;
            var visual = obj?["visual"] as JObject;
            if (!moving && draft.BrushAsset != null)
                visual = new JObject { ["model"] = draft.BrushAsset };
            bool box = moving ? obj != null && obj.Parent == draft.Room["boxes"] : draft.Tool == RoomTool.Box;
            string prefab = box ? RoomPreview.BoxModel((string)draft.Room["themeId"],obj) : "LayoutModels/Target";
            if (!moving && draft.Tool == RoomTool.Floor) prefab = "LayoutModels/Floor";
            if (!moving && draft.Tool == RoomTool.Wall) prefab = "LayoutModels/Wall";
            string key = draft.ActiveId + prefab + visual;
            if (!model || previous != key)
            {
                Hide();
                if (!tint) tint = new Material(Resources.Load<Material>("LayoutModels/GhostSurface")) { hideFlags = HideFlags.HideAndDontSave };
                model = visual?["model"] != null ? new GameObject() : Object.Instantiate(Resources.Load<GameObject>(prefab));
                if (prefab == "LayoutModels/Wall" && visual?["model"] == null) model.transform.localScale = new Vector3(1, 2.6f, 1);
                model.name = "对象放置预览";
                foreach (var child in model.GetComponentsInChildren<Transform>(true)) child.gameObject.hideFlags = HideFlags.HideAndDontSave;
                if (visual != null)
                {
                    var appearance = model.AddComponent<ImportedAppearance>();
                    appearance.Apply(draft.Room, visual);
                    appearance.SetGhost(tint);
                }
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
                {
                    var slots = renderer.sharedMaterials;
                    for (int i=0; i<slots.Length; i++) slots[i] = tint;
                    renderer.sharedMaterials = slots;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                previous = key;
            }
            var color = error == null ? Color.green : Color.red;
            color.a = .43f;
            tint.SetColor("_BaseColor",color);
            model.transform.position = new Vector3(world.x,.025f,world.y);
        }
        public void Hide()
        {
            if (!model) return;
            model.SetActive(false);
            DestroyTransient(model);
            model = null;
        }
        public void Dispose() { Hide(); if(tint) DestroyTransient(tint); tint=null; }
        static void DestroyTransient(Object obj) { if(Application.isPlaying) Object.Destroy(obj); else Object.DestroyImmediate(obj); }
    }
}
