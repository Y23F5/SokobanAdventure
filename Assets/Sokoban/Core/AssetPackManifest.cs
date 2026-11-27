/**
 * @file AssetPackManifest.cs
 * @brief Parse 读取 GLB 路径、尺寸、占格、分类和主题；显式分类优先，缺少分类的原型清单使用原有规则。
 * ReadLabel(record, key, allowed, file)：读取可选类别或主题并校验其值。
 * SizeIssue 按类别评估实测水平包围盒；墙地和门要求 1±0.15 格，箱子和目标仅检查上限。
 * VariantIndex 把房间 ID 和房间格坐标混入固定散列，保证不同进程中同一格选中同一索引。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class AssetPackPart
    {
        public string File { get; }
        public string Category { get; }
        public string Theme { get; }
        public IReadOnlyList<double> Bounds { get; }
        public Cell GridFootprint { get; }
        public Cell? LogicalFootprint { get; }
        public Cell? SourceCell { get; }

        internal AssetPackPart(string file, string category, string theme, double[] bounds, Cell gridFootprint,
            Cell? logicalFootprint, Cell? sourceCell)
        {
            File = file;
            Category = category;
            Theme = theme;
            Bounds = Array.AsReadOnly(bounds);
            GridFootprint = gridFootprint;
            LogicalFootprint = logicalFootprint;
            SourceCell = sourceCell;
        }
    }

    public static class AssetPackManifest
    {
        public const string Entrance = "entry";
        public const string Exit = "exit";
        public const string Decoration = "decoration";
        public const string Other = "other";

        static readonly Dictionary<string, string> Themes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "depot", "warehouse" }, { "foundry", "factory" }, { "island-forest", "forest" }
        };

        static readonly HashSet<string> Categories = new HashSet<string>(StringComparer.Ordinal)
        {
            ObjectCategories.Box, ObjectCategories.Target, TerrainCategories.Wall, TerrainCategories.Floor,
            Entrance, Exit, Decoration, Other
        };

        static readonly HashSet<string> ThemeIds = new HashSet<string>(Themes.Values, StringComparer.Ordinal);

        public static IReadOnlyList<AssetPackPart> Parse(string text)
        {
            if (!(MiniJson.Parse(text, true) is Dictionary<string, object> root)
                || !root.TryGetValue("assets", out var value) || !(value is List<object> entries))
                throw new FormatException("素材包清单缺少 assets 数组");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parts = new List<AssetPackPart>(entries.Count);
            foreach (var entry in entries)
            {
                if (!(entry is Dictionary<string, object> record) || !record.TryGetValue("file", out var fileValue) || !(fileValue is string original))
                    throw new FormatException("素材记录缺少 file");
                string file = original.Replace('\\', '/');
                string[] components = file.Split('/');
                if (!file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || components.Any(c => c.Length == 0 || c == "." || c == ".." || c.Contains(":")))
                    throw new FormatException("素材包 GLB 路径无效：" + original);
                if (!seen.Add(file)) throw new FormatException("素材包 GLB 路径重复：" + file);
                string stem = components[components.Length - 1];
                stem = stem.Substring(0, stem.Length - 4).ToLowerInvariant();
                string room = record.TryGetValue("room", out var roomValue) ? roomValue as string : null;
                bool roomPart = components.Length > 2 && components[0] == "rooms";
                if (room == null && roomPart) room = components[1];
                string theme = ReadLabel(record, "theme", ThemeIds, file);
                if (theme == null && room != null && Themes.TryGetValue(room, out var mapped)) theme = mapped;
                string category = ReadLabel(record, "category", Categories, file) ?? Category(stem, roomPart);
                double[] bounds = Vector(record, "bounds_size_xyz", 3, file);
                if (bounds.Any(axis => axis < 0)) throw new FormatException(file + " 包围盒尺寸不能为负数");
                Cell grid = CellField(record, "grid_footprint_xz", file, false, true).Value;
                Cell? logical = CellField(record, "logical_footprint_xz", file, true, true);
                Cell? source = CellField(record, "source_cell_xz", file, true, false);
                parts.Add(new AssetPackPart(file, category, theme, bounds, grid, logical, source));
            }
            return parts.AsReadOnly();
        }

        static string ReadLabel(Dictionary<string, object> record, string key, HashSet<string> allowed, string file)
        {
            if (!record.TryGetValue(key, out var value)) return null;
            if (value is string label && allowed.Contains(label)) return label;
            throw new FormatException(file + " 的 " + key + " 无效");
        }

        static string Category(string stem, bool roomPart)
        {
            if (!roomPart) return Decoration;
            if (stem == "walls" || stem == "floor" || stem == "backdrop") return Other;
            if (stem.StartsWith("wall-cell", StringComparison.Ordinal)) return TerrainCategories.Wall;
            if (stem.StartsWith("floor-cell", StringComparison.Ordinal)) return TerrainCategories.Floor;
            if (stem.StartsWith("target", StringComparison.Ordinal)) return ObjectCategories.Target;
            if (stem == "entrance") return Entrance;
            if (stem == "exit") return Exit;
            return Decoration;
        }

        static double[] Vector(Dictionary<string, object> record, string key, int length, string file)
        {
            if (!record.TryGetValue(key, out var value) || !(value is List<object> list) || list.Count != length
                || list.Any(item => !(item is long) && !(item is double)))
                throw new FormatException(file + " 的 " + key + " 格式无效");
            return list.Select(item => Convert.ToDouble(item, CultureInfo.InvariantCulture)).ToArray();
        }

        static Cell? CellField(Dictionary<string, object> record, string key, string file, bool optional, bool footprint)
        {
            if (optional && (!record.TryGetValue(key, out var value) || value == null)) return null;
            var pair = Vector(record, key, 2, file);
            if (pair.Any(axis => axis != Math.Truncate(axis) || axis < (footprint ? 1 : int.MinValue) || axis > int.MaxValue))
                throw new FormatException(file + " 的 " + key + " 需要有效的整数坐标");
            return new Cell((int)pair[0], (int)pair[1]);
        }

        public static string SizeIssue(string category, string file, double width, double depth)
        {
            bool strict = category == TerrainCategories.Wall || category == TerrainCategories.Floor || category == Entrance || category == Exit;
            bool upperOnly = category == ObjectCategories.Box || category == ObjectCategories.Target;
            if (!strict && !upperOnly) return null;
            if (width <= 1.1500001 && depth <= 1.1500001 && (!strict || width >= 0.8499999 && depth >= 0.8499999)) return null;
            string size = width.ToString("0.####", CultureInfo.InvariantCulture) + "×" + depth.ToString("0.####", CultureInfo.InvariantCulture);
            return file + " 实测宽深 " + size + " 格，要求" + (strict ? "宽深各 0.85–1.15 格" : "宽深均不超过 1.15 格");
        }

        public static int VariantIndex(string roomId, Cell cell, int count)
        {
            if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in roomId) hash = (hash ^ c) * 16777619;
                hash = (hash ^ (uint)cell.X) * 16777619;
                hash = (hash ^ (uint)cell.Y) * 16777619;
                return (int)(hash % (uint)count);
            }
        }
    }
}
