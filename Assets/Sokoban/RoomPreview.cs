/**
 * @file RoomPreview.cs
 * CellRotation 将房间的四档顺时针旋转映射到 Unity 的世界 Y 轴。
 * @brief Sync 按草稿版本与查看范围同步房间，修复外部 Transform 变更；BuildRoom 按房间数据的地形网格、对象表与外观引用生成实例，不追加任何主题藤蔓或主题模型。
 * CreateRoot 和 Model 建立临时层级，Track 缓存模型的设计姿态，Marker 生成选择轮廓；AppearanceIssue 向两个编辑界面提供当前场景的资源诊断。
 * BoxModel、BrushModel：按外观引用与主题给出内置预制件路径。
 * ShowPlacement 使用同一房间模型生成候选虚影；CreateOutline 沿房间外缘标记候选和原有占地。
 * HidePlacement、Clear 和 DestroyTemporary 清理会话私有实例和材质。CellFromRay 拾取地平面格。
 */
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Sokoban.Core;

namespace Sokoban
{
    public sealed class RoomPreview
    {
        readonly List<(Transform part, Vector3 position, Vector3 scale, Quaternion rotation)> poses =
            new List<(Transform, Vector3, Vector3, Quaternion)>();
        readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        GameObject root, ghost;
        Material ghostMaterial, outlineMaterial;
        LineRenderer originalOutline;
        RoomDraft last;
        int version = -1, ghostVersion = -1;
        string visibleRoom, ghostRoom;
        bool tracking = true;
        Transform settling;
        ImportedAppearance settlingAppearance;
        ImportedAppearance[] appearances = System.Array.Empty<ImportedAppearance>();
        public string AppearanceIssue => appearances.Where(part => part).Select(part => part.Error ?? part.FootprintIssue ?? part.AnimationIssue).FirstOrDefault(issue => issue != null);
        Vector3 rest;
        float landedAt;
        public bool Settling => settling;

        public void Sync(RoomDraft draft, string onlyRoom = null)
        {
            if (!root || last != draft || version != draft.Revision || visibleRoom != onlyRoom)
            {
                DestroyTemporary(root);
                poses.Clear();
                root = CreateRoot("Room • " + (draft.World == null ? draft.ActiveId : draft.World.WorldId), null, Vector3.zero);
                if (draft.World == null) BuildRoom(draft.Model, draft.Origin, draft.Selected, root.transform);
                else foreach (var entry in draft.World.Rooms)
                {
                    if (onlyRoom != null && entry.LevelId != onlyRoom) continue;
                    BuildRoom(draft.Catalog[entry.LevelId], entry.Origin, entry.LevelId == draft.ActiveId ? draft.Selected : null, root.transform, draft.World, draft.Catalog);
                }
                settling = null;
                appearances = root.GetComponentsInChildren<ImportedAppearance>(true);
                if (last == draft && version != draft.Revision && draft.PlacementRevision == draft.Revision)
                {
                    var roomRoot = root.transform.Find(draft.ActiveId);
                    settling = roomRoot?.Find(draft.PlacedObject);
                    if (settling)
                    {
                        rest = settling.localPosition;
                        settlingAppearance = settling.GetComponent<ImportedAppearance>();
                        landedAt = -1f;
                    }
                }
                last = draft;
                version = draft.Revision;
                visibleRoom = onlyRoom;
            }
            foreach (var p in poses)
            {
                if (!p.part) { version = -1; continue; }
                if (!p.part.hasChanged) continue;
                p.part.localPosition = p.position;
                p.part.localRotation = p.rotation;
                p.part.localScale = p.scale;
                p.part.hasChanged = false;
            }
            if (settling)
            {
                if (settlingAppearance && settlingAppearance.Ready != null && !settlingAppearance.Ready.IsCompleted)
                {
                    settling.localPosition = rest + Vector3.up * .22f;
                    return;
                }
                if (landedAt < 0f) landedAt = Time.realtimeSinceStartup;
                float t = Mathf.Clamp01((Time.realtimeSinceStartup - landedAt) / .20f);
                settling.localPosition = rest + Vector3.up * (.22f * (1 - t) * (1 - t));
                if (t >= 1) settling = null;
            }
        }

        void Track(Transform t)
        {
            t.gameObject.hideFlags = HideFlags.DontSave;
            if (tracking) poses.Add((t, t.localPosition, t.localScale, t.localRotation));
        }

        void Retrack(Transform part)
        {
            if (!tracking) return;
            int i = poses.FindLastIndex(p => p.part == part);
            poses[i] = (part, part.localPosition, part.localScale, part.localRotation);
        }

        GameObject CreateRoot(string name, Transform parent, Vector3 local)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            Track(go.transform);
            return go;
        }

        Transform Model(string kind, string name, Cell cell, Transform parent, Quaternion rotation)
        {
            if (!prefabs.TryGetValue(kind, out var prefab))
            {
                prefab = Resources.Load<GameObject>(kind.Contains("/") ? kind : "LayoutModels/" + kind);
                if (!prefab) throw new System.InvalidOperationException("缺少内置模型 LayoutModels/" + kind);
                prefabs.Add(kind, prefab);
            }
            var model = Object.Instantiate(prefab, parent, false);
            model.name = name;
            model.transform.localPosition = new Vector3(cell.X, 0, cell.Y);
            model.transform.localRotation = rotation;
            foreach (var part in model.GetComponentsInChildren<Transform>(true)) Track(part);
            return model.transform;
        }

        internal static Quaternion CellRotation(int quarterTurns) => Quaternion.Euler(0, quarterTurns * 90f, 0);

        public static string BoxModel(string theme, AppearanceRef look = null) =>
            AppearanceLibrary.Prefab(AppearanceLibrary.Resolve(ObjectCategories.Box, theme, look));

        public static string BrushModel(string categoryId, string theme, int variant = 0) =>
            AppearanceLibrary.Prefab(AppearanceLibrary.Default(categoryId, theme, variant) ?? AppearanceLibrary.Default(ObjectCategories.Target, theme));

        public Transform BuildRoom(RoomDraftModel model, Cell origin, string selected, Transform parent, WorldDocument world = null, IReadOnlyDictionary<string, RoomDraftModel> catalog = null)
        {
            var room = model.Room;
            var group = CreateRoot(room.Id, parent, new Vector3(origin.X, 0, origin.Y)).transform;
            string theme = model.Meta.ThemeId;
            string foundationMaterial = "LayoutModels/" + (theme == "factory" ? "Stone" : theme == "forest" ? "Moss" : "Kraft");
            bool solid = true;
            for (int y = 0; y < room.Height && solid; y++)
                for (int x = 0; x < room.Width; x++)
                    if (room.TerrainAt(new Cell(x, y)).CategoryId == TerrainCategories.Void) { solid = false; break; }

            Transform Place(AppearanceRef look, string name, Cell cell, Quaternion rotation)
            {
                string path = AppearanceLibrary.Prefab(look);
                if (path != null) return Model(path, name, cell, group, rotation);
                var anchor = CreateRoot(name, group, new Vector3(cell.X, 0, cell.Y));
                anchor.transform.localRotation = rotation;
                Retrack(anchor.transform);
                if (look != null) anchor.AddComponent<ImportedAppearance>().Apply(model.Assets, look.Id);
                return anchor.transform;
            }

            if (solid)
            {
                var baseModel = Model("Floor", "地基", new Cell(0, 0), group, Quaternion.identity);
                baseModel.localPosition = new Vector3((room.Width - 1) * .5f, -.15f, (room.Height - 1) * .5f);
                baseModel.localScale = new Vector3(room.Width, 3, room.Height);
                baseModel.GetComponentInChildren<Renderer>().sharedMaterial = Resources.Load<Material>(foundationMaterial);
                Retrack(baseModel);
            }
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                {
                    var cell = new Cell(x, y);
                    var terrain = room.TerrainAt(cell);
                    string category = terrain.CategoryId;
                    var rotation = category == TerrainCategories.Door ? Quaternion.identity : CellRotation(terrain.Rotation);
                    if (category == TerrainCategories.Void) continue;
                    if (!solid)
                    {
                        var foundation = Model("Floor", "地基 " + cell, cell, group, Quaternion.identity);
                        foundation.localPosition += Vector3.down * .15f;
                        foundation.localScale = new Vector3(1, 3, 1);
                        foundation.GetComponentInChildren<Renderer>().sharedMaterial = Resources.Load<Material>(foundationMaterial);
                        Retrack(foundation);
                    }
                    var look = room.AppearanceAt(cell);
                    if (category == TerrainCategories.Wall)
                    {
                        look = look ?? AppearanceLibrary.Default(TerrainCategories.Wall, theme);
                        var wall = Place(look, "Wall " + cell, cell, rotation);
                        if (AppearanceLibrary.Prefab(look) == "LayoutModels/Wall") { wall.localScale = new Vector3(1, 2.6f, 1); Retrack(wall); }
                    }
                    else if (category == TerrainCategories.Openable)
                        Place(look ?? AppearanceLibrary.Default(TerrainCategories.Openable, theme), "Vine " + cell, cell, rotation);
                    else Place(look ?? AppearanceLibrary.Default(TerrainCategories.Floor, theme), "Floor " + cell, cell, rotation);
                }
            foreach (var door in model.Doors)
            {
                var inward = new Vector3(-door.Outward.X, 0, -door.Outward.Y);
                var look = room.AppearanceAt(door.Cell) ?? AppearanceLibrary.Default(TerrainCategories.Door, theme);
                Place(look, door.Id, door.Cell, inward == Vector3.zero ? Quaternion.identity : Quaternion.LookRotation(inward));
            }
            foreach (var item in room.Objects)
            {
                var cell = item.Cell;
                string category = item.CategoryId;
                AppearanceRef look = item.Appearance;
                if (category == ObjectCategories.Spawn) look = new AppearanceRef("Spawn");
                else if (category == ObjectCategories.Box || category == ObjectCategories.Target) look = AppearanceLibrary.Resolve(category, theme, look);
                if (model.ObjectStates.TryGetValue(item.Id, out var table))
                {
                    var anchor = CreateRoot(item.Id, group, new Vector3(cell.X, 0, cell.Y));
                    anchor.transform.localRotation = CellRotation(item.Rotation);
                    Retrack(anchor.transform);
                    string fallback = AppearanceLibrary.Prefab(look) ?? AppearanceLibrary.Prefab(AppearanceLibrary.Default(ObjectCategories.Box, theme));
                    anchor.AddComponent<ObjectStateView>().Configure(table, new Vector3(origin.X + cell.X, 0, origin.Y + cell.Y), look, fallback, model.Assets);
                }
                else Place(look, item.Id, cell, CellRotation(item.Rotation));
                if (selected == item.Id) Marker(group, cell);
            }
            foreach (var decoration in model.Decorations)
                if (selected == decoration.Id) Marker(group, decoration.Cell);
            if (tracking) RoomYard.Build(model, new Vector2Int(origin.X, origin.Y), group, world, catalog);
            return group;
        }

        void Marker(Transform parent, Cell cell)
        {
            Model("Selection", "Selection", cell, parent, Quaternion.identity);
        }

        public void ShowPlacement(RoomDraft draft, Vector2Int origin, Color color)
        {
            if (!ghost || ghostVersion != draft.Revision || ghostRoom != draft.ActiveId)
            {
                HidePlacement();
                tracking = false;
                try
                {
                    ghost = CreateRoot("房间放置预览", null, Vector3.zero);
                    BuildRoom(draft.Model, new Cell(0, 0), null, ghost.transform);
                }
                finally { tracking = true; }
                ghostVersion = draft.Revision;
                ghostRoom = draft.ActiveId;
                ghostMaterial = new Material(Resources.Load<Material>("LayoutModels/GhostSurface"));
                foreach (var renderer in ghost.GetComponentsInChildren<Renderer>())
                {
                    renderer.sharedMaterial = ghostMaterial;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                foreach (var appearance in ghost.GetComponentsInChildren<ImportedAppearance>(true)) appearance.SetGhost(ghostMaterial);
                outlineMaterial = new Material(Resources.Load<Material>("LayoutModels/GhostSurface"));
                CreateOutline("候选占地", draft, false);
                originalOutline = CreateOutline("原有占地", draft, true);
            }
            ghost.transform.position = new Vector3(origin.x, 0f, origin.y);
            outlineMaterial.SetColor("_BaseColor", color);
            originalOutline.enabled = draft.Placement != null && new Cell(origin.x, origin.y) != draft.Origin;
            originalOutline.transform.position = new Vector3(draft.Origin.X, 0, draft.Origin.Y);
            color.a = .48f;
            ghostMaterial.SetColor("_BaseColor", color);
        }

        LineRenderer CreateOutline(string label, RoomDraft draft, bool original)
        {
            var part = new GameObject(label);
            part.hideFlags = HideFlags.DontSave;
            part.transform.SetParent(ghost.transform, false);
            var line = part.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.sharedMaterial = outlineMaterial;
            line.startWidth = line.endWidth = .08f;
            if (original)
            {
                var props = new MaterialPropertyBlock();
                props.SetColor("_BaseColor", new Color(.25f, .8f, 1f));
                line.SetPropertyBlock(props);
            }
            float left = -.52f, right = draft.Room.Width - .48f, bottom = -.52f, top = draft.Room.Height - .48f;
            line.positionCount = 4;
            line.SetPositions(new[] { new Vector3(left, .25f, bottom), new Vector3(right, .25f, bottom),
                new Vector3(right, .25f, top), new Vector3(left, .25f, top) });
            return line;
        }

        public void HidePlacement()
        {
            DestroyTemporary(ghost);
            DestroyTemporary(ghostMaterial);
            DestroyTemporary(outlineMaterial);
            ghost = null;
            ghostMaterial = null;
            ghostVersion = -1;
        }

        static void DestroyTemporary(Object item)
        {
            if (!item) return;
            if (item is GameObject go) go.SetActive(false);
            if (Application.isPlaying) Object.Destroy(item); else Object.DestroyImmediate(item);
        }

        public static bool CellFromRay(Ray ray, out Vector2Int cell)
        {
            cell = default;
            if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float distance)) return false;
            var hit = ray.GetPoint(distance);
            cell = new Vector2Int(Mathf.RoundToInt(hit.x), Mathf.RoundToInt(hit.z));
            return true;
        }

        public void Clear()
        {
            HidePlacement();
            DestroyTemporary(root);
            root = null;
            poses.Clear();
            last = null;
            version = -1;
        }
    }
}
