/**
 * @file RoomPreview.cs
 * @brief Sync 按草稿版本与查看范围同步房间，修复外部 Transform 变更；BuildRoom 按整数格生成内置 Prefab。
 * CreateRoot 和 Model 建立临时层级，Track 缓存模型的设计姿态，Marker 生成选择轮廓。
 * ShowPlacement 使用同一房间模型生成候选虚影；CreateOutline 沿房间外缘标记候选和原有占地。
 * HidePlacement、Clear 和 DestroyTemporary 清理会话私有实例和材质。CellFromRay 拾取地平面格。
 */
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

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
        Vector3 rest;
        float landedAt;
        public bool Settling => settling;


        public void Sync(RoomDraft draft, string onlyRoom = null)
        {
            if (!root || last != draft || version != draft.Revision || visibleRoom != onlyRoom)
            {
                DestroyTemporary(root);
                poses.Clear();
                root = CreateRoot("Room • " + (draft.World == null ? draft.ActiveId : (string)draft.World["worldId"]), null, Vector3.zero);
                if (draft.World == null) BuildRoom(draft.Room, Vector2Int.zero, draft.Selected, root.transform);
                else foreach (var entry in draft.World["rooms"])
                {
                    string id = (string)entry["levelId"];
                    if (onlyRoom != null && id != onlyRoom) continue;
                    BuildRoom((JObject)draft.Catalog[id], RoomFile.Cell(entry["origin"]), id == draft.ActiveId ? draft.Selected : null, root.transform);
                }
                settling=null;
                if(last==draft && version!=draft.Revision && draft.PlacementRevision==draft.Revision)
                {
                    var roomRoot=root.transform.Find(draft.ActiveId);
                    settling=roomRoot?.Find(draft.PlacedObject);
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
            if(settling)
            {
                if (settlingAppearance && settlingAppearance.Ready != null && !settlingAppearance.Ready.IsCompleted)
                {
                    settling.localPosition = rest + Vector3.up * .22f;
                    return;
                }
                if (landedAt < 0f) landedAt = Time.realtimeSinceStartup;
                float t=Mathf.Clamp01((Time.realtimeSinceStartup-landedAt)/.20f);
                settling.localPosition=rest+Vector3.up*(.22f*(1-t)*(1-t));
                if(t>=1) settling=null;
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
            poses[i] = (part,part.localPosition,part.localScale,part.localRotation);
        }

        GameObject CreateRoot(string name, Transform parent, Vector3 local)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            Track(go.transform);
            return go;
        }

        Transform Model(string kind, string name, Vector2Int cell, Transform parent, Quaternion rotation)
        {
            if (!prefabs.TryGetValue(kind, out var prefab))
            {
                prefab = Resources.Load<GameObject>(kind.Contains("/") ? kind : "LayoutModels/" + kind);
                if (!prefab) throw new System.InvalidOperationException("缺少内置模型 LayoutModels/" + kind);
                prefabs.Add(kind, prefab);
            }
            var model = Object.Instantiate(prefab, parent, false);
            model.name = name;
            model.transform.localPosition = new Vector3(cell.x, 0, cell.y);
            model.transform.localRotation = rotation;
            foreach (var part in model.GetComponentsInChildren<Transform>(true)) Track(part);
            return model.transform;
        }

        public static string BoxModel(string theme) => "Diorama/" + (theme == "factory" ? "Machinery" : theme == "forest" ? "Logs" : "Cargo");

        public Transform BuildRoom(JObject room, Vector2Int origin, string selected, Transform parent)
        {
            var group = CreateRoot((string)room["levelId"], parent, new Vector3(origin.x, 0, origin.y)).transform;
            var size = RoomFile.Size(room);
            string theme = (string)room["themeId"];
            bool factory = theme == "factory", forest = theme == "forest";
            bool solid = !room["terrainRowsNorthToSouth"].Any(row => ((string)row).Contains(RoomFile.Void));
            if (solid)
            {
                var baseModel = Model("Floor", "地基", Vector2Int.zero, group, Quaternion.identity);
                baseModel.localPosition = new Vector3((size.x-1)*.5f,-.15f,(size.y-1)*.5f);
                baseModel.localScale = new Vector3(size.x,3,size.y);
                baseModel.GetComponentInChildren<Renderer>().sharedMaterial = Resources.Load<Material>("LayoutModels/" + (factory ? "Stone" : forest ? "Moss" : "Kraft"));
                Retrack(baseModel);
            }
            for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (RoomFile.Terrain(room, cell) == RoomFile.Void) continue;
                    if (!solid)
                    {
                        var foundation = Model("Floor", "地基 " + cell, cell, group, Quaternion.identity);
                        foundation.localPosition += Vector3.down * .15f;
                        foundation.localScale = new Vector3(1, 3, 1);
                        foundation.GetComponentInChildren<Renderer>().sharedMaterial = Resources.Load<Material>("LayoutModels/" + (factory ? "Stone" : forest ? "Moss" : "Kraft"));
                        Retrack(foundation);
                    }
                    string kind = RoomFile.Floor(room, cell) ? "Floor" : "Wall";
                    var tile = Model(kind, kind + " " + cell, cell, group, Quaternion.identity);
                    if (kind == "Wall") { tile.localScale = new Vector3(1,2.6f,1); Retrack(tile); }
                    else if (factory || forest) tile.GetComponentInChildren<Renderer>().sharedMaterial = Resources.Load<Material>("LayoutModels/" + (factory ? "Stone" : "Sage"));
                }
            foreach (var item in RoomFile.Objects(room))
            {
                var cell = RoomFile.At(item);
                string id = (string)item["id"];
                string kind;
                Quaternion rotation = Quaternion.identity;
                if (item.Parent == room["boxes"]) kind = BoxModel(theme);
                else if (item.Parent == room["targets"]) kind = "Target";
                else if (item == room["spawn"]) kind = "Spawn";
                else if (item.Parent == room["decorations"]) kind = null;
                else
                {
                    kind = "Door";
                    Vector2Int inward = RoomFile.Inward(item);
                    rotation = Quaternion.LookRotation(new Vector3(inward.x, 0, inward.y));
                }
                var visual = item["visual"];
                if (kind == null || visual?["model"] != null)
                {
                    var anchor = CreateRoot(id, group, new Vector3(cell.x, 0, cell.y));
                    anchor.transform.localRotation = rotation;
                    Retrack(anchor.transform);
                    if (visual != null) anchor.AddComponent<ImportedAppearance>().Apply(room, visual);
                }
                else
                {
                    var builtin = Model(kind, id, cell, group, rotation);
                    if (visual != null) builtin.gameObject.AddComponent<ImportedAppearance>().Apply(room, visual);
                }
                if (selected == id) Marker(group, cell);
            }
            if (RoomFile.Terrain(room, new Vector2Int(1,size.y-1)) == RoomFile.Void) return group;
            var dressing = Model("Diorama/" + (factory ? "Conveyor" : forest ? "Tree" : "Lift"), "主题模型", new Vector2Int(1,size.y-1),group,Quaternion.Euler(0,-25,0));
            dressing.localPosition += Vector3.up*1.28f;
            Retrack(dressing);
            return group;
        }

        void Marker(Transform parent, Vector2Int cell)
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
                    BuildRoom(draft.Room, Vector2Int.zero, null, ghost.transform);
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
            originalOutline.enabled = draft.Placement != null && origin != draft.Origin;
            originalOutline.transform.position = new Vector3(draft.Origin.x, 0, draft.Origin.y);
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
                props.SetColor("_BaseColor", new Color(.25f,.8f,1f));
                line.SetPropertyBlock(props);
            }
            var size = RoomFile.Size(draft.Room);
            float left = -.52f, right = size.x-.48f, bottom = -.52f, top = size.y-.48f;
            line.positionCount = 4;
            line.SetPositions(new[] { new Vector3(left,.25f,bottom), new Vector3(right,.25f,bottom),
                new Vector3(right,.25f,top), new Vector3(left,.25f,top) });
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

        public static bool CellFromRay(Ray ray,out Vector2Int cell)
        {
            cell=default;
            if (!new Plane(Vector3.up,Vector3.zero).Raycast(ray,out float distance)) return false;
            var hit=ray.GetPoint(distance); cell=new Vector2Int(Mathf.RoundToInt(hit.x),Mathf.RoundToInt(hit.z)); return true;
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
