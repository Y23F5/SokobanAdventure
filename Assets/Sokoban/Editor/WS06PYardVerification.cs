/**
 * @file WS06PYardVerification.cs
 * @brief Run 加载正式世界，逐房生成地绘，验证 Unity 纹理、网格、邻接裁边和原型标牌尺寸。
 */
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class WS06PYardVerification
    {
        public static void Run()
        {
            var catalog = new JObject();
            var world = WorldLayout.Read(Path.Combine(Application.streamingAssetsPath,"World/main.world.json"),catalog);
            var root = new GameObject();
            try
            {
                float previousEdge = float.NegativeInfinity;
                var preview = new RoomPreview();
                foreach (var row in world["rooms"].OrderBy(r => RoomFile.Cell(r["origin"]).x))
                {
                    string id = (string)row["levelId"];
                    var group = preview.BuildRoom((JObject)catalog[id],RoomFile.Cell(row["origin"]),null,root.transform,world,catalog);
                    var yard = group.Find("外围地绘");
                    var bounds = yard.GetComponent<Renderer>().bounds;
                    var texture = yard.GetComponent<Renderer>().sharedMaterial.GetTexture("_BaseMap");
                    RoomFile.Check(yard.GetComponent<MeshFilter>().sharedMesh && texture && bounds.size.x > 1 && bounds.min.x >= previousEdge-.001f,"地绘缺失或交叠 " + id);
                    previousEdge = bounds.max.x;
                    var scenery = id == "warehouse" ? new[] { "RackRow" } : id == "factory"
                        ? new[] { "TankRow", "SidePipes" } : new[] { "PineBank", "StoneBank" };
                    foreach (var name in scenery)
                    {
                        var model = group.Find(name);
                        RoomFile.Check(model && model.localScale == Vector3.one, "外围模型缺失或比例改变 " + name);
                        var box = model.GetComponent<Renderer>().bounds;
                        var size = RoomFile.Size((JObject)catalog[id]);
                        RoomFile.Check(box.max.z < group.position.z - .5f || box.min.z > group.position.z + size.y - .5f,
                            "外围模型侵入房间 " + name);
                        Debug.Log("SCENERY PASS " + name + " bounds=" + box);
                    }
                    if (id == "warehouse")
                    {
                        var props = group.Find("调度岗亭与装卸区");
                        RoomFile.Check(props && props.GetComponentsInChildren<Collider>().Length == 0,"外围陈设缺失或含有碰撞体");
                        var mesh = props.GetComponent<MeshFilter>().sharedMesh;
                        foreach (var door in catalog[id]["doors"])
                        {
                            var cell = RoomFile.At(door);
                            RoomFile.Check(!mesh.vertices.Any(v => Mathf.Abs(v.x-cell.x)<.51f && Mathf.Abs(v.z-cell.y)<.51f),"陈设进入门格");
                        }
                        Debug.Log("DEPOT PROPS PASS triangles=" + mesh.triangles.Length/3);
                    }
                    if (id == "warehouse") RoomFile.Check(group.Find("仓库标牌").GetComponent<Renderer>().sharedMaterial.GetTexture("_BaseMap"),"标牌纹理缺失");
                    Debug.Log($"YARD PASS {id} {texture.name} {texture.width}x{texture.height} x=[{bounds.min.x},{bounds.max.x}]");
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
