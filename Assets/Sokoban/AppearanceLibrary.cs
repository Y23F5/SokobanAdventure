/**
 * @file AppearanceLibrary.cs
 * @brief 内置外观库按类别 ID 分类，外观引用统一用 Core.AppearanceRef；主题只提供默认外观与推荐，不决定房间里有什么；素材页签按类别 ID 归类。
 * AppearanceLibrary.Entry(categoryId, id, name, prefab, theme, variants)：一条内置外观，Prefab(variant) 按变体序号取 Resources 路径，单变体时忽略序号。
 * AppearanceLibrary.Decoration、Remnant：装饰与残留物两个外观类别 ID，不属于对象层；其余类别 ID 取自地形与对象注册表。
 * AppearanceLibrary.Themes、ThemeName(id)、TypeName(categoryId)：主题 ID 列表与显示名、类别显示名，显示名与 ID 分开。
 * AppearanceLibrary.Find(id)、Entries(categoryId, theme)：按 ID 查条目；按类别列出全部条目，当前主题的推荐排在前面。
 * AppearanceLibrary.Default(categoryId, theme, variant)：主题给出的默认外观引用，地板按传入的变体序号取变体，序号由调用方写入数据而非按坐标哈希。
 * AppearanceLibrary.Resolve(categoryId, theme, explicitRef)：有显式引用时原样返回，否则取主题默认。
 * AppearanceLibrary.Prefab(look)：内置外观返回 Prefab 路径，导入模型或未知 ID 返回空，由调用方改走 ImportedAppearance。
 * PaletteTabs：地形、物件、装饰、其他、自定义五个页签 ID，CategoriesOf(tab) 返回页签包含的类别 ID，其他页本轮为空。
 * RoomDraft.BrushCategory、BrushAppearance、BrushTemplate、ClearBrush()：当前放置画刷的类别、外观与对象模板，切换工具时由调用方清空。
 * RoomDraft.ChooseAppearance(id, categoryId, variant)：选择内置外观或本房已有的导入模型作为画刷，不修改房间。
 * RoomDraft.SelectedCategory：所选格上对象的类别；没有对象时为墙外装饰（decoration）或该格地形类别。
 * RoomDraft.CanReplaceAppearance(id)、ReplaceAppearance(id, variant)：外观类别与所选内容一致时，经一条可撤销命令改写地形、对象或装饰的外观引用。
 * RoomDraft.SetTheme(theme)：改写 RoomMeta 的主题；同一条命令里先把未写外观的地形、箱子与目标冻结为旧主题默认值，已放内容外观不变。
 * RoomDraft.PaletteItems(tab)：页签内的候选项；地形与物件页为内置外观，装饰页为内置装饰加装饰类导入模型，自定义页为对象模板与导入的 GLB 模型。
 * RoomDraft.SetObjectAppearance(model, id, look)：移除后以同 ID、同格、新外观重新登记对象，并原样保留其对象状态表。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;

namespace Sokoban
{
    public static class AppearanceLibrary
    {
        public const string Decoration = "decoration";
        public const string Remnant = "remnant";

        public sealed class Entry
        {
            public readonly string CategoryId, Id, Name, Theme;
            public readonly int Variants;
            readonly string prefab;

            internal Entry(string categoryId, string id, string name, string prefab, string theme = null, int variants = 1)
            {
                CategoryId = categoryId; Id = id; Name = name; this.prefab = prefab; Theme = theme; Variants = variants;
            }

            public string Prefab(int variant = 0) => Variants > 1 ? prefab + ((variant % Variants + Variants) % Variants) : prefab;
        }

        public static readonly string[] Themes = { "warehouse", "factory", "forest" };

        public static string ThemeName(string id) => id == "factory" ? "工厂" : id == "forest" ? "森林" : id == "warehouse" ? "仓库" : id;

        public static string TypeName(string categoryId) => categoryId switch
        {
            ObjectCategories.Box => "箱子", ObjectCategories.Target => "目标点", ObjectCategories.Spawn => "出生点", ObjectCategories.Custom => "自定义物件",
            TerrainCategories.Floor => "地板", TerrainCategories.Wall => "墙壁", TerrainCategories.Door => "门", TerrainCategories.Openable => "藤门",
            Decoration => "装饰", Remnant => "残留物", _ => "其他"
        };

        static readonly Entry[] Builtins = new[]
        {
            new Entry(ObjectCategories.Box, "Cargo", "仓库木箱", "Diorama/Cargo", "warehouse"),
            new Entry(ObjectCategories.Box, "Machinery", "工厂货箱", "Diorama/Machinery", "factory"),
            new Entry(ObjectCategories.Box, "Logs", "森林原木", "Diorama/Logs", "forest"),
            new Entry(ObjectCategories.Box, "ForestTree", "完整树木", "Diorama/Tree", "forest"),
            new Entry(ObjectCategories.Target, "Target", "目标点", "LayoutModels/Target", "warehouse"),
            new Entry(ObjectCategories.Target, "FactoryTarget", "工厂目标点", "LayoutModels/FactoryTarget", "factory"),
            new Entry(ObjectCategories.Target, "ForestTarget", "森林目标点", "LayoutModels/ForestTarget", "forest"),
            new Entry(ObjectCategories.Spawn, "Spawn", "出生点", "LayoutModels/Spawn"),
            new Entry(TerrainCategories.Wall, "DepotWall", "仓库木墙", "LayoutModels/Wall", "warehouse"),
            new Entry(TerrainCategories.Wall, "FactoryWall", "工厂金属墙", "LayoutModels/FactoryWall", "factory"),
            new Entry(TerrainCategories.Wall, "ForestWall", "森林青苔石墙", "LayoutModels/ForestWall", "forest"),
            new Entry(TerrainCategories.Floor, "warehouseFloor", "仓库地板", "Diorama/warehouseTile", "warehouse", 3),
            new Entry(TerrainCategories.Floor, "factoryFloor", "工厂地板", "Diorama/factoryTile", "factory", 3),
            new Entry(TerrainCategories.Floor, "forestFloor", "森林地板", "Diorama/forestTile", "forest", 3),
            new Entry(TerrainCategories.Floor, "Floor", "素面地板", "LayoutModels/Floor"),
            new Entry(TerrainCategories.Door, "Door", "门", "LayoutModels/Door"),
            new Entry(TerrainCategories.Openable, "Vine", "藤门", "Diorama/ForestVine"),
            new Entry(Decoration, "ForestFence", "森林木篱笆", "LayoutModels/ForestFence", "forest"),
            new Entry(Decoration, "ForestVine", "森林藤篱", "Diorama/ForestVine", "forest"),
            new Entry(Remnant, "TreeStump", "树桩", "LayoutModels/TreeStump", "forest")
        };

        public static Entry Find(string id) => id == null ? null : Builtins.FirstOrDefault(entry => entry.Id == id);

        public static IEnumerable<Entry> Entries(string categoryId, string theme) => Builtins.Where(entry => entry.CategoryId == categoryId)
            .OrderByDescending(entry => theme != null && entry.Theme == theme);

        public static AppearanceRef Default(string categoryId, string theme, int variant = 0)
        {
            var candidates = Builtins.Where(entry => entry.CategoryId == categoryId).ToList();
            var entry = candidates.FirstOrDefault(e => e.Theme == theme) ?? candidates.FirstOrDefault(e => e.Theme == null) ?? candidates.FirstOrDefault();
            if (entry == null) return null;
            return new AppearanceRef(entry.Id, entry.Variants > 1 ? (variant % entry.Variants + entry.Variants) % entry.Variants : 0);
        }

        public static AppearanceRef Resolve(string categoryId, string theme, AppearanceRef explicitRef = null) =>
            explicitRef ?? Default(categoryId, theme);

        public static string Prefab(AppearanceRef look) => Find(look?.Id)?.Prefab(look.Variant);
    }

    public static class PaletteTabs
    {
        public const string Terrain = "terrain";
        public const string Object = "object";
        public const string Decoration = "decoration";
        public const string Other = "other";
        public const string Custom = "custom";
        public static readonly string[] All = { Terrain, Object, Decoration, Other, Custom };

        public static string Name(string tab) => tab switch
        {
            Terrain => "地形", Object => "物件", Decoration => "装饰", Other => "其他", Custom => "自定义", _ => tab
        };

        public static IReadOnlyList<string> CategoriesOf(string tab) => tab switch
        {
            Terrain => new[] { TerrainCategories.Floor, TerrainCategories.Wall, TerrainCategories.Door, TerrainCategories.Openable, AssetPackManifest.Entrance, AssetPackManifest.Exit },
            Object => new[] { ObjectCategories.Box, ObjectCategories.Target, ObjectCategories.Spawn },
            Decoration => new[] { AppearanceLibrary.Decoration },
            Custom => new[] { ObjectCategories.Custom },
            Other => new[] { AssetPackManifest.Other },
            _ => Array.Empty<string>()
        };
    }

    public sealed class PaletteItem
    {
        public readonly string Tab, CategoryId, Id, Name, Prefab, AssetId, TemplateId;

        public PaletteItem(string tab, string categoryId, string id, string name, string prefab = null, string assetId = null, string templateId = null)
        {
            Tab = tab; CategoryId = categoryId; Id = id; Name = name; Prefab = prefab; AssetId = assetId; TemplateId = templateId;
        }
    }

    public sealed partial class RoomDraft
    {
        public string BrushCategory { get; private set; }
        public AppearanceRef BrushAppearance { get; private set; }
        public string BrushTemplate { get; private set; }

        public void ClearBrush()
        {
            BrushCategory = null;
            BrushAppearance = null;
            BrushTemplate = null;
        }

        public string Theme => Model.Meta?.ThemeId;

        public void ChooseAppearance(string appearanceId, string categoryId, int variant = 0)
        {
            var entry = AppearanceLibrary.Find(appearanceId);
            if (entry != null)
            {
                if (entry.CategoryId != categoryId) throw new InvalidOperationException("素材与类别不符");
            }
            else if (AssetDefinition(appearanceId)?.Kind != AssetKinds.Model) throw new InvalidOperationException("当前房间没有所选素材");
            ClearBrush();
            BrushCategory = categoryId;
            BrushAppearance = new AppearanceRef(appearanceId, variant);
        }

        public string SelectedCategory
        {
            get
            {
                if (!SelectedCell.HasValue) return null;
                var cell = SelectedCell.Value;
                var placed = Room.ObjectAt(cell);
                if (placed != null) return placed.CategoryId;
                if (Model.Decorations.Any(d => d.Cell == cell)) return AppearanceLibrary.Decoration;
                string terrain = Room.TerrainAt(cell).CategoryId;
                return terrain == TerrainCategories.Void ? null : terrain;
            }
        }

        public bool CanReplaceAppearance(string id)
        {
            string category = SelectedCategory;
            if (category == null || category == ObjectCategories.Spawn) return false;
            var entry = AppearanceLibrary.Find(id);
            if (entry != null) return entry.CategoryId == category;
            return AssetDefinition(id)?.Kind == AssetKinds.Model;
        }

        public void ReplaceAppearance(string id, int variant = 0)
        {
            if (!CanReplaceAppearance(id)) throw new InvalidOperationException("请先选择同类对象或对应的地形格");
            var look = new AppearanceRef(id, variant);
            var cell = SelectedCell.Value;
            var model = Model;
            var room = Room;
            var copy = AssetCopy(id);
            var placed = room.ObjectAt(cell);
            var decoration = placed == null ? model.Decorations.FirstOrDefault(d => d.Cell == cell) : null;
            if (placed != null)
            {
                if (Equals(placed.Appearance, look)) return;
                var before = placed.Appearance;
                Commit(new ModelEdit(() => { copy.Apply(model); SetObjectAppearance(model, placed.Id, look); },
                    () => { SetObjectAppearance(model, placed.Id, before); copy.Revert(model); }));
            }
            else if (decoration != null)
            {
                if (Equals(decoration.Appearance, look)) return;
                var after = new Decoration(decoration.Id, decoration.Cell, look, decoration.Rotation);
                Commit(new ModelEdit(() => { copy.Apply(model); Swap(model.Decorations, decoration, after); },
                    () => { Swap(model.Decorations, after, decoration); copy.Revert(model); }));
            }
            else
            {
                var before = room.AppearanceAt(cell);
                if (Equals(before, look)) return;
                Commit(new ModelEdit(() => { copy.Apply(model); room.SetAppearance(cell, look); },
                    () => { room.SetAppearance(cell, before); copy.Revert(model); }));
            }
            Select(cell);
        }

        static void Swap<T>(IList<T> list, T from, T to) where T : class
        {
            int index = list.IndexOf(from);
            if (index >= 0) list[index] = to;
        }

        internal static void SetObjectAppearance(RoomDraftModel model, string id, AppearanceRef look)
        {
            var placed = model.Room.Objects.First(o => o.Id == id);
            model.ObjectStates.TryGetValue(id, out var states);
            model.Room.RemoveObject(id);
            model.Room.AddObject(id, placed.CategoryId, placed.Cell, look, placed.Rotation);
            if (states != null) model.ObjectStates[id] = states;
        }

        public void SetTheme(string theme)
        {
            if (!AppearanceLibrary.Themes.Contains(theme)) throw new InvalidOperationException("未知房间主题");
            string previous = Theme;
            if (theme == previous) return;
            var model = Model;
            var room = Room;
            var beforeMeta = model.Meta;
            var afterMeta = beforeMeta.WithIdentity(beforeMeta.Name, theme);
            var frozenCells = new List<(Cell cell, AppearanceRef look)>();
            for (int x = 0; x < room.Width; x++)
                for (int y = 0; y < room.Height; y++)
                {
                    var cell = new Cell(x, y);
                    string category = room.TerrainAt(cell).CategoryId;
                    if (category == TerrainCategories.Void || room.AppearanceAt(cell) != null) continue;
                    var look = AppearanceLibrary.Default(category, previous);
                    if (look != null) frozenCells.Add((cell, look));
                }
            var frozenObjects = room.Objects.Where(o => o.Appearance == null && (o.CategoryId == ObjectCategories.Box || o.CategoryId == ObjectCategories.Target))
                .Select(o => (o.Id, look: AppearanceLibrary.Default(o.CategoryId, previous))).ToList();
            Commit(new ModelEdit(() =>
            {
                foreach (var f in frozenCells) room.SetAppearance(f.cell, f.look);
                foreach (var f in frozenObjects) SetObjectAppearance(model, f.Id, f.look);
                model.Meta = afterMeta;
            }, () =>
            {
                model.Meta = beforeMeta;
                foreach (var f in frozenObjects) SetObjectAppearance(model, f.Id, null);
                foreach (var f in frozenCells) room.SetAppearance(f.cell, null);
            }));
        }

        public IReadOnlyList<PaletteItem> PaletteItems(string tab)
        {
            var items = new List<PaletteItem>();
            string theme = Theme;
            if (tab == PaletteTabs.Custom)
            {
                foreach (var template in Model.Templates.Values)
                    items.Add(new PaletteItem(tab, ObjectCategories.Custom, template.Id, template.Name + " · 对象模板",
                        AppearanceLibrary.Prefab(template.Appearance), template.Appearance?.Id != null && AppearanceLibrary.Find(template.Appearance.Id) == null ? template.Appearance.Id : null, template.Id));
                foreach (var asset in ModelAssets())
                    items.Add(new PaletteItem(tab, ObjectCategories.Custom, asset.Id, asset.Name, null, asset.Id));
                return items;
            }
            var pack = PackParts().ToArray();
            foreach (string category in PaletteTabs.CategoriesOf(tab))
            {
                foreach (var item in pack.Where(item => item.category == category).OrderByDescending(item => item.theme == theme)
                    .ThenBy(item => item.asset.Name, StringComparer.Ordinal))
                    items.Add(new PaletteItem(tab, category, item.asset.Id, item.asset.Name, null, item.asset.Id));
                foreach (var entry in AppearanceLibrary.Entries(category, theme))
                    items.Add(new PaletteItem(tab, category, entry.Id, entry.Name, entry.Prefab()));
                if (tab == PaletteTabs.Decoration)
                    foreach (var asset in ModelAssets().Where(asset => !pack.Any(item => item.asset.Id == asset.Id)))
                        items.Add(new PaletteItem(tab, category, asset.Id, asset.Name, null, asset.Id));
            }
            return items;
        }

        IEnumerable<AssetDescriptor> ModelAssets() => Model.Assets.Where(a => a.Kind == AssetKinds.Model);
    }
}
