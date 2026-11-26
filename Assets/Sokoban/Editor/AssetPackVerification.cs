/**
 * @file AssetPackVerification.cs
 * @brief Run 导入原型素材包并逐项实测，写出分类与拒绝报告，验证笔划、旋转、高度、撤销与文件重开。
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
                const string source = "PlayTest3/assets/glb/ground-centered/manifest.json";
                string output = Path.GetFullPath("Docs/验收证据/重建/阶段5");
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
                draft.Open("Docs/关卡样例/WS-01.json", true);
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

        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            Debug.Log("PACKQA PASS " + message);
        }
    }
}
