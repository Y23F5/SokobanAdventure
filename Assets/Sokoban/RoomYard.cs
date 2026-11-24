/**
 * @file RoomYard.cs
 * @brief Build 按原型尺寸在房间地基下铺设主题地绘，并在仓库外沿放置标牌。Clip 对相邻房间在间隙中点裁切；Surface 建立无碰撞的贴图平面。
 */
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sokoban
{
    internal static class RoomYard
    {
        internal static void Build(JObject room, Vector2Int origin, Transform group, JObject world, JObject catalog)
        {
            string texture = (string)room["themeId"] switch { "warehouse" => "depot", "factory" => "foundry", "forest" => "lagoon", _ => null };
            if (texture == null || room["spawn"] == null) return;
            var size = RoomFile.Size(room);
            var center = ((Vector2)size - Vector2.one) * .5f;
            var dimensions = texture == "lagoon" ? new Vector2(24 * size.x / 11f,20 * size.y / 6f) : new Vector2(24,20);
            var low = center - dimensions * .5f;
            var high = center + dimensions * .5f;
            if (world != null)
                foreach (var row in world["rooms"])
                {
                    string id = (string)row["levelId"];
                    if (id == (string)room["levelId"]) continue;
                    var other = RoomFile.Cell(row["origin"]) - origin;
                    var otherSize = RoomFile.Size((JObject)catalog[id]);
                    if (other.x >= size.x || other.x + otherSize.x <= 0) Clip(ref low.x, ref high.x, size.x, other.x, otherSize.x);
                    else Clip(ref low.y, ref high.y, size.y, other.y, otherSize.y);
                }
            var yard = Surface(group,"外围地绘",texture,new Vector3((high.x+low.x)*.5f,-.741f,(high.y+low.y)*.5f), Quaternion.Euler(90,0,0),high-low);
            var uv = (low - center) / dimensions + Vector2.one * .5f;
            var scale = (high-low) / dimensions;
            var block = new MaterialPropertyBlock();
            block.SetVector("_BaseMap_ST",new Vector4(scale.x,scale.y,uv.x,uv.y));
            yard.SetPropertyBlock(block);
            if (texture == "depot") Surface(group,"仓库标牌","depot-plate",new Vector3(center.x,1.08f,-1.06f),Quaternion.Euler(20,0,0),new Vector2(1.72f,.484f));
        }

        static void Clip(ref float low, ref float high, int size, int other, int otherSize)
        {
            if (other >= size) high = Mathf.Min(high,(size + other - 1) * .5f);
            if (other + otherSize <= 0) low = Mathf.Max(low,(other + otherSize - 1) * .5f);
        }

        static Renderer Surface(Transform parent,string name,string texture,Vector3 position,Quaternion rotation,Vector2 size)
        {
            var part = new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
            part.hideFlags = HideFlags.DontSave;
            part.transform.SetParent(parent,false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = new Vector3(size.x,size.y,1);
            part.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Resources.Load<Material>("Diorama/Yards/"+texture);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }
    }
}
