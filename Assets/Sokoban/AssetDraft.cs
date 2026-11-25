/**
 * @file AssetDraft.cs
 * @brief ImportAsset registers a verified payload as one undoable change. BindAsset assigns a typed reference to an object or room HUD.
 * SelectAsset chooses a model brush without changing the design. PlaceAsset adds a one-cell decoration with a stable identity.
 * CanBindAsset guards the two palettes; AssetBindingTarget reports the bound room or selected cell. Uses Commit for history, dirty tracking and both editor projections.
 */
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public sealed partial class RoomDraft
    {
        public int PlacementRevision { get; private set; } = -1;
        public string PlacedObject { get; private set; }
        void NotifyPlacement(string id) { PlacedObject=id; PlacementRevision=Revision; }
        public string BrushAsset { get; private set; }
        public string ImportAsset(string path,string kind)
        {
            return RegisterAsset(RoomAssets.Import(path,kind));
        }
        internal string RegisterAsset(JObject asset)
        {
            string kind = (string)asset["kind"];
            string id=(string)asset["id"];
            var next=(JObject)Room.DeepClone();
            if(next["assets"]==null) next["assets"]=new JObject();
            if (next["assets"][id] == null) next["assets"][id] = asset;
            if (kind == "model") RegisterCategory(next, Tool, id);
            if (JToken.DeepEquals(Room, next)) return id;
            Commit(next,Selected);
            return id;
        }
        static void RegisterCategory(JObject room, RoomTool tool, string id)
        {
            if (!IsAppearanceTool(tool) && tool != RoomTool.Decoration) return;
            string category = tool.ToString().ToLowerInvariant();
            var catalog = room["appearanceCatalog"] ??= new JObject();
            var entries = catalog[category] ??= new JObject();
            entries[id] = new JObject { ["model"] = id };
        }
        public static bool IsAppearanceTool(RoomTool tool) => tool == RoomTool.Box || tool == RoomTool.Target || tool == RoomTool.Wall || tool == RoomTool.Floor;
        JObject RequireAsset(string id)
        {
            var asset = AssetDefinition(id);
            RoomFile.Check(asset!=null,"当前房间没有所选素材");
            return asset;
        }
        public static readonly string[] BuiltinBoxes = { "Cargo", "Machinery", "Logs" };
        public static readonly string[] BuiltinBoxNames = { "仓库木箱", "工厂货箱", "森林原木" };
        public bool BoxSelected => Selected != null && Find(Selected)?.Parent == Room["boxes"];
        public void BindBuiltinBox(string model)
        {
            RoomFile.Check(BoxSelected && Array.IndexOf(BuiltinBoxes,model) >= 0,"请选择箱子和内置箱体模型");
            ReplaceAppearance(model);
        }
        public bool CanBindAsset(string id) => AssetDefinition(id) is JObject asset &&
            ((string)asset["kind"] == "uiImage" || SelectedCell is Vector2Int cell && RoomFile.Terrain(Room, cell) != RoomFile.Void);

        public bool CanBindAsset(string id, RoomTool category) => CanBindAsset(id) && (category == RoomTool.Select ||
            (category == RoomTool.Wall || category == RoomTool.Floor ? SelectedCell is Vector2Int cell &&
                RoomFile.Terrain(Room, cell) == (category == RoomTool.Wall ? '#' : '.') : SelectedAppearanceType == category));

        public string AssetBindingTarget(string id)
        {
            if (!CanBindAsset(id)) return "选择地图上的物件或地形格后，可为它绑定素材";
            if ((string)AssetDefinition(id)["kind"] == "uiImage") return "绑定位置：当前房间界面";
            var cell = SelectedCell.Value;
            string target = Selected ?? (RoomFile.Terrain(Room, cell) == '#' ? "墙壁" : "地板");
            return "绑定位置：" + target + " " + cell;
        }

        public void BindAsset(string id, RoomTool category = RoomTool.Select)
        {
            var kind=(string)RequireAsset(id)["kind"];
            RoomFile.Check(CanBindAsset(id, category), "请选择同类型的对象或地形格");
            var next=(JObject)Room.DeepClone();
            CopyAssetInto(next, id);
            if(kind=="uiImage") next["uiImage"]=id;
            else
            {
                var point = SelectedCell.Value;
                bool terrain = category == RoomTool.Wall || category == RoomTool.Floor;
                var item = terrain ? TerrainRecord(next, point) : PropertyObject(next, point);
                if (item == null)
                {
                    if (next["terrainObjects"] == null) next["terrainObjects"] = new JArray();
                    item = new JObject { ["id"] = "tile-" + Guid.NewGuid().ToString("N"), ["cell"] = RoomFile.Point(point) };
                    ((JArray)next["terrainObjects"]).Add(item);
                }
                if (item["visual"] == null) item["visual"] = new JObject();
                if (kind == "model") ((JObject)item).Remove("builtinModel");
                item["visual"][kind] = id;
                if (kind == "model") RegisterCategory(next, category, id);
            }
            if (JToken.DeepEquals(Room, next)) return;
            var selectedCell = SelectedCell;
            Commit(next, Selected);
            if (selectedCell.HasValue) Select(selectedCell.Value);
        }
        public void SelectAsset(string id) => SelectAsset(id, RoomTool.Decoration);

        public void SelectAsset(string id, RoomTool category)
        {
            RoomFile.Check((string)RequireAsset(id)["kind"]=="model","放置工具需要模型素材");
            RoomFile.Check(category == RoomTool.Box || category == RoomTool.Target || category == RoomTool.Wall || category == RoomTool.Floor || category == RoomTool.Decoration, "此工具不支持模型素材");
            Tool = category;
            BrushAsset = id;
        }
        public void PlaceAsset(Vector2Int cell)
        {
            if (BrushBuiltin == null) RequireAsset(BrushAsset);
            string error=PlacementError(false,cell);
            RoomFile.Check(error==null,error);
            var next=(JObject)Room.DeepClone();
            if(next["decorations"]==null) next["decorations"]=new JArray();
            string id="decor-"+Guid.NewGuid().ToString("N");
            var instance = new JObject { ["id"]=id,["cell"]=RoomFile.Point(cell) };
            ApplyBrush(next, instance, RoomTool.Decoration, cell);
            AddDefinedObject(next, instance, id);
            ((JArray)next["decorations"]).Add(instance);
            Commit(next,id);
            NotifyPlacement(id);
        }
    }
}
