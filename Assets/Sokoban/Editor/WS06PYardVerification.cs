/**
 * @file WS06PYardVerification.cs
 * @brief Run 加载正式世界，逐房生成预览，验证外围地绘的纹理与邻接裁边、主题地砖的位置与网格尺寸、门的位置，并确认不再按主题固定生成任何外围陈设、标牌或设备。
 * VerifyFloorTiles：逐个地板格检查地砖位置、网格名称与尺寸，网格种类数必须等于数据里外观引用解析出的预制件种类数，变体序号来自数据而非坐标。
 * VerifyDoors：每扇门按门 ID 存在于房间层级，位置等于门格。
 * Require：把验收差异转成批处理失败。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Sokoban.Core;

namespace Sokoban.Editor
{
    public static class WS06PYardVerification
    {
        static readonly string[] RemovedFixtures = { "RackRow", "TankRow", "SidePipes", "PineBank", "StoneBank", "主题模型", "仓库标牌", "调度岗亭与装卸区" };

        static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        static void VerifyFloorTiles(RoomDraftModel model, Transform group)
        {
            var room = model.Room;
            var meshes = new HashSet<Mesh>();
            var prefabs = new HashSet<string>();
            int count = 0;
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                {
                    var cell = new Cell(x, y);
                    if (room.TerrainAt(cell).CategoryId != TerrainCategories.Floor) continue;
                    var tile = group.Find("Floor " + cell);
                    Require(tile && tile.localPosition == new Vector3(x, 0, y), "主题地砖与格坐标不一致 " + cell);
                    var mesh = tile.GetComponentInChildren<MeshFilter>().sharedMesh;
                    float height = mesh.bounds.max.y;
                    Require(mesh.name.StartsWith(model.Meta.ThemeId + "Tile") &&
                        Mathf.Abs(mesh.bounds.size.x - .986f) < .001f && Mathf.Abs(mesh.bounds.size.z - .986f) < .001f &&
                        height > 0 && height < .05f, "主题地砖网格尺寸或表面高度异常 " + cell);
                    meshes.Add(mesh);
                    prefabs.Add(AppearanceLibrary.Prefab(room.AppearanceAt(cell) ?? AppearanceLibrary.Default(TerrainCategories.Floor, model.Meta.ThemeId)));
                    count++;
                }
            Require(meshes.Count == prefabs.Count, "地面网格种类与数据外观不符 " + meshes.Count + "/" + prefabs.Count);
            Debug.Log($"FLOOR PASS {room.Id} cells={count} variants={meshes.Count}");
        }

        static void VerifyDoors(RoomDraftModel model, Transform group)
        {
            foreach (var door in model.Doors)
            {
                var node = group.Find(door.Id);
                Require(node && node.localPosition == new Vector3(door.Cell.X, 0, door.Cell.Y), "门位置错误 " + door.Id);
            }
        }

        public static void Run()
        {
            var catalog = new Dictionary<string, RoomDraftModel>(StringComparer.Ordinal);
            var world = WorldLayout.Read(Path.Combine(Application.streamingAssetsPath, "World/main.world.json"), catalog);
            var root = new GameObject();
            try
            {
                float previousEdge = float.NegativeInfinity;
                var preview = new RoomPreview();
                foreach (var row in world.Rooms.OrderBy(r => r.Origin.X))
                {
                    var model = catalog[row.LevelId];
                    var group = preview.BuildRoom(model, row.Origin, null, root.transform, world, catalog);
                    var yard = group.Find("外围地绘");
                    Require(yard, "缺少外围地绘 " + row.LevelId);
                    var bounds = yard.GetComponent<Renderer>().bounds;
                    var texture = yard.GetComponent<Renderer>().sharedMaterial.GetTexture("_BaseMap");
                    Require(yard.GetComponent<MeshFilter>().sharedMesh && texture && bounds.size.x > 1 && bounds.min.x >= previousEdge - .001f, "地绘缺失或交叠 " + row.LevelId);
                    previousEdge = bounds.max.x;
                    VerifyFloorTiles(model, group);
                    VerifyDoors(model, group);
                    foreach (string name in RemovedFixtures) Require(group.Find(name) == null, "不应再按主题固定生成 " + name);
                    Debug.Log($"YARD PASS {row.LevelId} {texture.name} {texture.width}x{texture.height} x=[{bounds.min.x},{bounds.max.x}]");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }
}
