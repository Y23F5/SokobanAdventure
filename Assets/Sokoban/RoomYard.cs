/**
 * @file RoomYard.cs
 * @brief Build 铺设房间外围地绘并摆放房间数据里的墙外装饰，不按主题固定摆放任何外围陈设、标牌或设备。
 * Build(model, origin, group, world, catalog)：地绘尺寸按房间格数换算，相邻房间在间隙中点裁切；装饰逐个取 Model.Decorations，按外观引用在墙外格位摆放内置或导入模型并应用旋转。
 * Ground：按主题取地绘贴图，主题只影响外观，没有对应贴图时不铺设。
 * Clip 对相邻房间在间隙中点裁切；Surface 建立无碰撞的贴图平面。
 */
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Sokoban.Core;

namespace Sokoban
{
    internal static class RoomYard
    {
        internal static void Build(RoomDraftModel model, Vector2Int origin, Transform group, WorldDocument world, IReadOnlyDictionary<string, RoomDraftModel> catalog)
        {
            Ground(model, origin, group, world, catalog);
            foreach (var decoration in model.Decorations)
            {
                string path = AppearanceLibrary.Prefab(decoration.Appearance);
                string imported = path == null && decoration.Appearance != null
                    && model.Assets.Any(asset => asset.Id == decoration.Appearance.Id && asset.Kind == AssetKinds.Model)
                    ? decoration.Appearance.Id : null;
                if (path == null && imported == null) continue;
                var part = ObjectStateView.Create(model.Assets, group, path, imported);
                part.name = decoration.Id;
                part.transform.localPosition = new Vector3(decoration.Cell.X, 0, decoration.Cell.Y);
                part.transform.localRotation = RoomPreview.CellRotation(decoration.Rotation);
            }
        }

        static void Ground(RoomDraftModel model, Vector2Int origin, Transform group, WorldDocument world, IReadOnlyDictionary<string, RoomDraftModel> catalog)
        {
            string texture = model.Meta.ThemeId switch { "warehouse" => "depot", "factory" => "foundry", "forest" => "lagoon", _ => null };
            if (texture == null) return;
            var size = new Vector2Int(model.Room.Width, model.Room.Height);
            var center = ((Vector2)size - Vector2.one) * .5f;
            var dimensions = texture == "lagoon" ? new Vector2(24 * size.x / 11f, 20 * size.y / 6f) : new Vector2(24, 20);
            var low = center - dimensions * .5f;
            var high = center + dimensions * .5f;
            if (world != null && catalog != null)
                foreach (var row in world.Rooms)
                {
                    if (row.LevelId == model.Room.Id || !catalog.TryGetValue(row.LevelId, out var other)) continue;
                    var offset = new Vector2Int(row.Origin.X, row.Origin.Y) - origin;
                    var otherSize = new Vector2Int(other.Room.Width, other.Room.Height);
                    if (offset.x >= size.x || offset.x + otherSize.x <= 0) Clip(ref low.x, ref high.x, size.x, offset.x, otherSize.x);
                    else Clip(ref low.y, ref high.y, size.y, offset.y, otherSize.y);
                }
            var yard = Surface(group, "外围地绘", texture, new Vector3((high.x + low.x) * .5f, -.741f, (high.y + low.y) * .5f), Quaternion.Euler(90, 0, 0), high - low);
            var uv = (low - center) / dimensions + Vector2.one * .5f;
            var scale = (high - low) / dimensions;
            var block = new MaterialPropertyBlock();
            block.SetVector("_BaseMap_ST", new Vector4(scale.x, scale.y, uv.x, uv.y));
            yard.SetPropertyBlock(block);
        }

        static void Clip(ref float low, ref float high, int size, int other, int otherSize)
        {
            if (other >= size) high = Mathf.Min(high, (size + other - 1) * .5f);
            if (other + otherSize <= 0) low = Mathf.Max(low, (other + otherSize - 1) * .5f);
        }

        static Renderer Surface(Transform parent, string name, string texture, Vector3 position, Quaternion rotation, Vector2 size)
        {
            var part = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            part.hideFlags = HideFlags.DontSave;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = new Vector3(size.x, size.y, 1);
            part.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("Diorama/Yards/" + texture);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }
    }
}
