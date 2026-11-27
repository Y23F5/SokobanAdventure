/**
 * @file AssetPackVerification.cs
 * @brief 导入原型素材包并逐项实测，验证素材登记、共享笔刷与背景贴图。
 * Run()：导入素材包并验证笔划、旋转、高度、撤销与文件重开。
 * VerifyBackdrops()：从指定清单导入全部素材，实例化三套背景并检查渲染材质有有效贴图。
 * Check(condition, message)：把断言记录到 Unity 日志，失败时抛出异常。
 */
using System;
using System.IO;
using System.Linq;
using Sokoban.Core;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class AssetPackVerification
    {
        public static async void Run()
        {
            try
            {
                string source = Environment.GetEnvironmentVariable("SOKOBAN_ASSET_PACK_MANIFEST")
                    ?? throw new InvalidOperationException("SOKOBAN_ASSET_PACK_MANIFEST 需要指向含显式分类的素材包清单");
                string output = Environment.GetEnvironmentVariable("SOKOBAN_ASSET_PACK_EVIDENCE")
                    ?? Path.GetFullPath("Docs/验收证据/重建/阶段5");
                Directory.CreateDirectory(output);
                var parts = AssetPackManifest.Parse(File.ReadAllText(source));
                var result = await AssetPackImporter.Import(source);
                File.WriteAllLines(Path.Combine(output, "pack-results.txt"), new[] { result.Summary }
                    .Concat(result.Accepted.Select(item => $"{item.part.Category}\t{item.part.Theme}\t{item.part.File}"))
                    .Concat(result.Rejected.Select(item => "拒绝\t" + item)));
                Check(parts.Count == result.Accepted.Count + result.Rejected.Count, "清单每项有明确结果");
                Check(result.Accepted.Any(item => item.part.Category == TerrainCategories.Wall)
                    && result.Accepted.Any(item => item.part.Category == TerrainCategories.Floor)
                    && result.Accepted.Any(item => item.part.Category == ObjectCategories.Box)
                    && result.Accepted.Any(item => item.part.Category == AssetPackManifest.Other), "分类通过，整房零件仅归其他");
                var draft = new LayoutPanel().Draft;
                draft.Place(true, new Cell(3, 2), "box-pack-qa");
                draft.Place(false, new Cell(4, 2), "target-pack-qa");
                draft.RegisterPack(result);
                int registered = draft.PackParts().Count();
                Check(registered > 0, "分类已经登记");
                draft.Undo();
                Check(!draft.PackParts().Any(), "一次撤销撤回整包登记");
                draft.Redo();
                Check(draft.PackParts().Count() == registered, "重做恢复整包登记");
                var cell = new Cell(3, 3);
                var recipe = draft.TerrainBrush(TerrainCategories.Floor);
                var expected = recipe(cell);
                draft.RotateBrush(1);
                Check(draft.Brush(new[] { cell }, RoomTool.Floor) == null && expected.Equals(draft.Room.AppearanceAt(cell)), "铺地写入变体");
                draft.SetAssetHeight(expected.Id, 0.125f);
                string path = Path.Combine(output, "pack.room.json");
                draft.Save(path, false);
                draft.Open(path, false);
                Check(expected.Equals(draft.Room.AppearanceAt(cell)) && draft.Room.TerrainAt(cell).Rotation == 1
                    && draft.AssetDefinition(expected.Id).HeightOffset == 0.125f && draft.PackParts().Count() == registered, "重开保留分类、变体、旋转和高度");
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        public static async void VerifyBackdrops()
        {
            try
            {
                string source = Environment.GetEnvironmentVariable("SOKOBAN_ASSET_PACK_MANIFEST") ??
                    "PlayTest3/assets/glb/ground-centered/manifest.json";
                var parts = AssetPackManifest.Parse(File.ReadAllText(source));
                var result = await AssetPackImporter.Import(source);
                Check(result.Accepted.Count == parts.Count && result.Rejected.Count == 0,
                    result.Summary + (result.Rejected.Count == 0 ? "" : " · " + string.Join(" · ", result.Rejected)));
                foreach (string room in new[] { "depot", "foundry", "island-forest" })
                {
                    var backdrop = result.Accepted.Single(item => item.part.File.Replace('\\', '/') == "rooms/" + room + "/backdrop.glb");
                    var stage = new GameObject("Backdrop check " + room) { hideFlags = HideFlags.HideAndDontSave };
                    try
                    {
                        var appearance = stage.AddComponent<ImportedAppearance>();
                        appearance.Apply(new[] { backdrop.asset }, backdrop.asset.Id);
                        await appearance.Ready;
                        Check(appearance.Error == null, room + " 背景模型可实例化");
                        var materials = stage.GetComponentsInChildren<Renderer>(true).SelectMany(renderer => renderer.sharedMaterials);
                        Check(materials.Any(material => material && material.mainTexture &&
                            material.mainTexture.width > 1 && material.mainTexture.height > 1), room + " 背景材质有贴图");
                    }
                    finally { UnityEngine.Object.DestroyImmediate(stage); }
                }
                EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                EditorApplication.Exit(1);
            }
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Debug.Log("PACKQA PASS " + message);
        }
    }
}
