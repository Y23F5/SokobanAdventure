/**
 * @file AppearanceLibrary.cs
 * @brief Entries enumerates built-in appearance IDs independent of the room. Recommended and Resolve supply defaults for legacy room data.
 * ChooseAppearance and ApplyBrush record an explicit visual binding, copying imported descriptors into the destination room for exchange.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public static class AppearanceLibrary
    {
        public sealed class Entry
        {
            public readonly RoomTool Type;
            public readonly string Id, Name, Prefab, Theme;
            internal Entry(RoomTool type, string id, string name, string prefab, string theme = null)
            { Type = type; Id = id; Name = name; Prefab = prefab; Theme = theme; }
        }

        public static readonly string[] Themes = { "warehouse", "factory", "forest" };
        public static string ThemeName(string id) => id == "factory" ? "工厂" : id == "forest" ? "森林" : id == "warehouse" ? "仓库" : id;
        public static string TypeName(RoomTool tool) => tool switch
        {
            RoomTool.Box => "箱子", RoomTool.Wall => "墙壁", RoomTool.Floor => "地板", RoomTool.Target => "目标点", _ => "其他"
        };

        static readonly Entry[] Builtins = new[]
        {
            new Entry(RoomTool.Box, "Cargo", "仓库木箱", "Diorama/Cargo", "warehouse"),
            new Entry(RoomTool.Box, "Machinery", "工厂货箱", "Diorama/Machinery", "factory"),
            new Entry(RoomTool.Box, "Logs", "森林原木", "Diorama/Logs", "forest"),
            new Entry(RoomTool.Wall, "DepotWall", "仓库木墙", "LayoutModels/Wall", "warehouse"),
            new Entry(RoomTool.Wall, "FactoryWall", "工厂金属墙", "LayoutModels/FactoryWall", "factory"),
            new Entry(RoomTool.Wall, "ForestWall", "森林青苔石墙", "LayoutModels/ForestWall", "forest"),
            new Entry(RoomTool.Target, "Target", "目标点", "LayoutModels/Target"),
            new Entry(RoomTool.Target, "FactoryTarget", "工厂目标点", "LayoutModels/FactoryTarget", "factory"),
            new Entry(RoomTool.Target, "ForestTarget", "森林目标点", "LayoutModels/ForestTarget", "forest"),
            new Entry(RoomTool.Decoration, "ForestFence", "森林木篱笆", "LayoutModels/ForestFence", "forest"),
            new Entry(RoomTool.Decoration, "ForestVine", "森林藤篱", "Diorama/ForestVine", "forest")
        }.Concat(Themes.SelectMany(theme => Enumerable.Range(0, 3).Select(i =>
            new Entry(RoomTool.Floor, theme + "Tile" + i, ThemeName(theme) + "地板 " + (i + 1), "Diorama/" + theme + "Tile" + i, theme))))
            .Append(new Entry(RoomTool.Floor, "Floor", "素面地板", "LayoutModels/Floor")).ToArray();

        public static Entry Find(string id) => Builtins.FirstOrDefault(entry => entry.Id == id);
        public static IEnumerable<Entry> Entries(RoomTool type, string theme) => Builtins.Where(entry => entry.Type == type)
            .OrderByDescending(entry => entry.Theme == theme);
        public static Entry Resolve(RoomTool type, string theme, Vector2Int cell, JToken record = null)
        {
            var explicitEntry = Find((string)record?["builtinModel"]);
            if (explicitEntry != null) return explicitEntry;
            if (type == RoomTool.Floor)
            {
                int variant = Mathf.Abs((cell.x * 17 + cell.y * 29 + cell.x * cell.y) % 3);
                return Find(theme + "Tile" + variant) ?? Find("Floor");
            }
            if (type == RoomTool.Target) return Find("Target");
            return Entries(type, theme).FirstOrDefault();
        }

        public static RoomTool TypeOf(JObject room, JToken item)
        {
            if (item == null) return RoomTool.Select;
            if (item.Parent == room["boxes"]) return RoomTool.Box;
            if (item.Parent == room["targets"]) return RoomTool.Target;
            if (item.Parent == room["decorations"]) return RoomTool.Decoration;
            if (item.Parent == room["terrainObjects"])
                return RoomFile.Floor(room, RoomFile.At(item)) ? RoomTool.Floor : RoomTool.Wall;
            return RoomTool.Select;
        }
    }

    public sealed partial class RoomDraft
    {
        public string BrushBuiltin { get; private set; }
        public JObject AssetDefinition(string id) => id == null ? null : Room["assets"]?[id] as JObject ?? Catalog.Properties()
            .Select(p => p.Value["assets"]?[id] as JObject).FirstOrDefault(asset => asset != null);
        public JObject AssetRoom(string id) => id != null && Room["assets"]?[id] == null && AssetDefinition(id) is JObject asset
            ? new JObject { ["assets"] = new JObject { [id] = asset.DeepClone() } } : Room;

        public IEnumerable<string> CategoryAssets(RoomTool type)
        {
            string key = type.ToString().ToLowerInvariant();
            return Catalog.Properties().SelectMany(p => (p.Value["appearanceCatalog"]?[key] as JObject)?.Properties()
                ?? Enumerable.Empty<JProperty>()).Select(p => (string)p.Value["model"]).Where(id => AssetDefinition(id) != null).Distinct();
        }

        public void ChooseAppearance(string builtin, RoomTool type)
        {
            RoomFile.Check(AppearanceLibrary.Find(builtin)?.Type == type, "素材与基础类型不符");
            Tool = type;
            BrushBuiltin = builtin;
        }

        void CopyAssetInto(JObject room, string id)
        {
            var descriptor = RequireAsset(id);
            if (room["assets"] == null) room["assets"] = new JObject();
            if (room["assets"][id] == null) room["assets"][id] = descriptor.DeepClone();
        }

        void ApplyBrush(JObject room, JObject item, RoomTool type, Vector2Int cell, string builtin = null)
        {
            if (Tool == type && BrushAsset != null && builtin == null)
            {
                CopyAssetInto(room, BrushAsset);
                RegisterCategory(room, type, BrushAsset);
                item.Remove("builtinModel");
                if (item["visual"] == null) item["visual"] = new JObject();
                item["visual"]["model"] = BrushAsset;
                return;
            }
            string id = builtin ?? (Tool == type ? BrushBuiltin : null) ??
                AppearanceLibrary.Resolve(type, (string)room["themeId"], cell)?.Id;
            if (id == null) return;
            item["builtinModel"] = id;
            if (item["visual"] is JObject visual)
            {
                visual.Remove("model");
                if (!visual.HasValues) item.Remove("visual");
            }
        }

        public RoomTool SelectedAppearanceType
        {
            get
            {
                if (!SelectedCell.HasValue) return RoomTool.Select;
                if (Find(Selected) is JToken item) return AppearanceLibrary.TypeOf(Room, item);
                return RoomFile.Terrain(Room, SelectedCell.Value) switch { '#' => RoomTool.Wall, '.' => RoomTool.Floor, _ => RoomTool.Select };
            }
        }

        public bool CanReplaceAppearance(string id)
        {
            var entry = AppearanceLibrary.Find(id);
            var type = SelectedAppearanceType;
            return entry != null && (type == entry.Type || (entry.Type == RoomTool.Floor || entry.Type == RoomTool.Wall) &&
                SelectedCell.HasValue && RoomFile.Terrain(Room, SelectedCell.Value) == (entry.Type == RoomTool.Floor ? '.' : '#'));
        }

        public void ReplaceAppearance(string id)
        {
            RoomFile.Check(CanReplaceAppearance(id), "请先选择同类对象或对应的地形格");
            var entry = AppearanceLibrary.Find(id);
            var cell = SelectedCell.Value;
            var next = (JObject)Room.DeepClone();
            var item = entry.Type == RoomTool.Wall || entry.Type == RoomTool.Floor ? TerrainRecord(next, cell) : (JObject)PropertyObject(next, cell);
            ApplyBrush(next, item, entry.Type, cell, id);
            if (!JToken.DeepEquals(next, Room)) { Commit(next, Selected); Select(cell); }
        }

        static JObject TerrainRecord(JObject room, Vector2Int cell)
        {
            if (room["terrainObjects"] == null) room["terrainObjects"] = new JArray();
            var records = (JArray)room["terrainObjects"];
            var record = records.OfType<JObject>().FirstOrDefault(item => RoomFile.At(item) == cell);
            if (record != null) return record;
            return AddTerrainRecord(records, cell);
        }

        static JObject AddTerrainRecord(JArray records, Vector2Int cell)
        {
            var record = new JObject { ["id"] = "tile-" + Guid.NewGuid().ToString("N"), ["cell"] = RoomFile.Point(cell) };
            records.Add(record);
            return record;
        }

        static void FreezeTerrainAppearances(JObject room)
        {
            string theme = (string)room["themeId"];
            var records = (JArray)(room["terrainObjects"] ??= new JArray());
            var existing = records.OfType<JObject>().ToDictionary(RoomFile.At);
            var size = RoomFile.Size(room);
            for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                {
                    var cell = new Vector2Int(x, y);
                    char tile = RoomFile.Terrain(room, cell);
                    if (tile == RoomFile.Void) continue;
                    if (existing.TryGetValue(cell, out var item) && (item["builtinModel"] != null || item["visual"]?["model"] != null)) continue;
                    var type = tile == '#' ? RoomTool.Wall : RoomTool.Floor;
                    (item ?? AddTerrainRecord(records, cell))["builtinModel"] = AppearanceLibrary.Resolve(type, theme, cell).Id;
                }
        }

        public void SetTheme(string theme)
        {
            RoomFile.Check(AppearanceLibrary.Themes.Contains(theme), "未知房间主题");
            string previous = (string)Room["themeId"];
            if (theme == previous) return;
            var next = (JObject)Room.DeepClone();
            FreezeTerrainAppearances(next);
            foreach (var type in new[] { RoomTool.Box, RoomTool.Target })
                foreach (var item in next[type == RoomTool.Box ? "boxes" : "targets"])
                    if (item["builtinModel"] == null && item["visual"]?["model"] == null)
                        item["builtinModel"] = AppearanceLibrary.Resolve(type, previous, RoomFile.At(item)).Id;
            next["themeId"] = theme;
            if (next["recoveryPresentation"] == null && RoomFile.RecoveryPresets.Contains(previous)) next["recoveryPresentation"] = previous;
            var selected = SelectedCell;
            Commit(next, Selected);
            if (selected.HasValue) Select(selected.Value);
        }
    }
}
