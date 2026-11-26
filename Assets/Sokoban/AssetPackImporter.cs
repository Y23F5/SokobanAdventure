/**
 * @file AssetPackImporter.cs
 * @brief Import 读取素材包清单，逐一实测 GLB 并保留通过项；RegisterPack 原子登记资源与分类元数据，撤销只移除该次新增项。
 * PackParts 枚举已登记的分类与主题，TerrainBrush 选择显式外观或计算可重现的主题变体。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sokoban.Core;

namespace Sokoban
{
    public static class AssetPackImporter
    {
        public sealed class Report
        {
            public readonly List<(AssetPackPart part, AssetDescriptor asset)> Accepted = new List<(AssetPackPart, AssetDescriptor)>();
            public readonly List<string> Rejected = new List<string>();
            public string Summary => $"已登记 {Accepted.Count} 项，拒绝 {Rejected.Count} 项";
        }

        public static async Task<Report> Import(string manifest)
        {
            var parts = AssetPackManifest.Parse(File.ReadAllText(manifest));
            string root = Path.GetDirectoryName(Path.GetFullPath(manifest));
            var report = new Report();
            foreach (var part in parts)
            {
                try
                {
                    string path = Path.Combine(root, part.File);
                    var asset = RoomAssets.Import(path, AssetKinds.Model);
                    await ImportedAppearance.InspectModel(asset, part.Category, part.File);
                    report.Accepted.Add((part, asset));
                }
                catch (Exception error) { report.Rejected.Add(part.File + " · " + error.Message); }
            }
            return report;
        }
    }

    public sealed partial class RoomDraft
    {
        const string PackPrefix = "assetPack/";

        public void RegisterPack(AssetPackImporter.Report report)
        {
            var model = Model;
            var assets = report.Accepted.Select(item => item.asset).GroupBy(asset => asset.Id).Select(group => group.First())
                .Where(asset => !model.Assets.Any(old => old.Id == asset.Id)).ToArray();
            var records = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in report.Accepted)
            {
                string key = PackPrefix + item.part.Category + "/" + item.part.Theme + "/" + item.asset.Id;
                if (model.Extras.ContainsKey(key) || records.ContainsKey(key)) continue;
                var footprint = item.part.LogicalFootprint ?? item.part.GridFootprint;
                records.Add(key, footprint.X.ToString(CultureInfo.InvariantCulture) + "," + footprint.Y.ToString(CultureInfo.InvariantCulture));
            }
            if (assets.Length == 0 && records.Count == 0) return;
            Commit(new ModelEdit(() =>
            {
                model.Assets.AddRange(assets);
                foreach (var pair in records) model.Extras.Add(pair.Key, pair.Value);
            }, () =>
            {
                foreach (var asset in assets) model.Assets.Remove(asset);
                foreach (var key in records.Keys) model.Extras.Remove(key);
            }), Selected);
        }

        public IEnumerable<(string category, string theme, AssetDescriptor asset)> PackParts()
        {
            var assets = Model.Assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
            foreach (var key in Model.Extras.Keys.Where(key => key.StartsWith(PackPrefix, StringComparison.Ordinal)))
            {
                var fields = key.Split('/');
                if (fields.Length == 4 && assets.TryGetValue(fields[3], out var asset))
                    yield return (fields[1], fields[2], asset);
            }
        }

        public Func<Cell, AppearanceRef> TerrainBrush(string category)
        {
            if (BrushCategory == category && BrushAppearance != null)
            {
                var selected = BrushAppearance;
                return _ => selected;
            }
            var candidates = PackParts().Where(item => item.category == category && item.theme == Theme)
                .Select(item => item.asset.Id).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            if (candidates.Length == 0) return null;
            string roomId = ActiveId;
            return cell =>
            {
                int index = AssetPackManifest.VariantIndex(roomId, cell, candidates.Length);
                return new AppearanceRef(candidates[index], index);
            };
        }
    }
}
