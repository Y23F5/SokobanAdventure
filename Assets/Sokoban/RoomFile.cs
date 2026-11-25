/**
 * @file RoomFile.cs
 * @brief 独立房间格式与原子文件读写。ForExport 按分类外观目录和独立功能定义选择导出版本，Parse 读取及校验版本，Upgrade 按端点转换旧地形；
 * Check 验证必需条件，Cell 解出整数格，Point 编码格，Objects 枚举有位置的实体，
 * Size/At 分别读取尺寸及实体格，Inside/Floor 按北到南行序检查格子，Inward 计算门内侧位置，
 * Validate 检查字段、地形与身份；StructureIssues 定位边界缺口、门及入口预留错误。
 * RequireStructure 在正式文件入口拒绝这些错误；Write 先写同目录临时文件再替换目标。
 */
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public static class RoomFile
    {
        public static JObject Parse(string json, bool draft = false)
        {
            var room = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if ((int?)room["formatVersion"] == 1 && ((string)room["kind"] ?? "room") == "room") Upgrade(room);
            int? version = (int?)room["formatVersion"];
            Check((string)room["kind"] == "room" && (version == 2 || version == 3), "需要有效的房间文件");
            Validate(room);
            RoomAssets.Validate(room);
            if (!draft) RequireStructure(room);
            return room;
        }

        public static JObject ForExport(JObject source)
        {
            var room = (JObject)source.DeepClone();
            foreach (string key in new[] { "appearanceCatalog", "objectDefinitions" })
            {
                if (room[key] == null) continue;
                Check(room[key] is JObject, key + " 必须是目录对象");
                if (room[key].HasValues) room["formatVersion"] = 3;
            }
            return room;
        }

        static void Upgrade(JObject room)
        {
            Check(room["connections"] is JArray links && links.Count == 0, "旧文件连接无法归属");
            var doors = (JArray)room["doors"];
            var entry = doors.Where(d => (string)d["role"] == "entry").ToList();
            Check(entry.Count == 1, "旧文件需要唯一入口");
            var rows = (JArray)room["terrainRowsNorthToSouth"];
            foreach (var door in doors)
            {
                var cell = At(door);
                Check(Inside(room, cell), "旧门越界");
                int row = rows.Count - 1 - cell.y;
                var line = ((string)rows[row]).ToCharArray();
                if (line[cell.x] == '<' || line[cell.x] == '>') line[cell.x] = '.';
                rows[row] = new string(line);
            }
            room.Remove("connections");
            room["kind"] = "room";
            room["formatVersion"] = 2;
            room["primaryEntranceId"] = (string)entry[0]["id"];
        }

        public static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }

        public static Vector2Int Cell(JToken value)
        {
            Check(value is JArray a && a.Count == 2 && a.All(t => t.Type == JTokenType.Integer), "需要两个整数坐标");
            return new Vector2Int((int)value[0], (int)value[1]);
        }
        public static JArray Point(Vector2Int cell) => new JArray(cell.x, cell.y);
        public static readonly string[] RecoveryPresets = { "warehouse", "factory", "forest" };
        public static string RecoveryPreset(JObject room) => (string)room["recoveryPresentation"] ?? (string)room["themeId"];
        public static Vector2Int Size(JObject room) => Cell(room["size"]);
        public static Vector2Int At(JToken entity) => Cell(entity["cell"]);
        public static IEnumerable<JToken> Objects(JObject room) => room["boxes"].Concat(room["targets"]).Append(room["spawn"]).Where(entity => entity != null).Concat(room["doors"]).Concat(room["decorations"] as JArray ?? new JArray());
        public static bool Inside(JObject room, Vector2Int cell)
        {
            var size = Size(room);
            return cell.x >= 0 && cell.y >= 0 && cell.x < size.x && cell.y < size.y;
        }
        public static char Terrain(JObject room, Vector2Int cell) => Inside(room, cell) ? ((string)room["terrainRowsNorthToSouth"][Size(room).y - 1 - cell.y])[cell.x] : Void;
        public const char Void = '_';
        public static bool Floor(JObject room, Vector2Int cell) => Terrain(room, cell) == '.';
        public static Vector2Int Inward(JToken door)
        {
            switch ((string)door["direction"])
            {
                case "N": return Vector2Int.down;
                case "S": return Vector2Int.up;
                case "W": return Vector2Int.right;
                case "E": return Vector2Int.left;
                default: throw new InvalidDataException("门朝向无效");
            }
        }
        static void Validate(JObject room)
        {
            foreach (var key in new[]{"levelId", "name", "themeId"})
                Check(room[key]?.Type == JTokenType.String && !string.IsNullOrWhiteSpace((string)room[key]), "缺少 " + key);
            Check(room["recoveryPresentation"] == null || RecoveryPresets.Contains((string)room["recoveryPresentation"]), "未知恢复演出");
            foreach (var key in new[]{"doors", "boxes", "targets", "events", "terrainRowsNorthToSouth"}) Check(room[key] is JArray, "需要数组 " + key);
            Check((room["spawn"] == null || room["spawn"] is JObject) && room["connections"] == null, "出生点或房间字段不正确");
            var size = Size(room);
            var rows = (JArray)room["terrainRowsNorthToSouth"];
            Check(size.x > 0 && size.y == rows.Count && size.y > 0 && rows.All(r => r.Type == JTokenType.String && ((string)r).Length == size.x && ((string)r).All(c => c == '#' || c == '.' || c == Void)), "地形与房间尺寸不一致");
            Check(room["decorations"]==null || room["decorations"] is JArray,"装饰列表格式错误");
            var ids = new HashSet<string>();
            var occupied = new HashSet<Vector2Int>();
            foreach (var obj in Objects(room))
            {
                var id = (string)obj["id"];
                Check(!string.IsNullOrWhiteSpace(id) && ids.Add(id), "无效或重复 ID " + id);
                Check(occupied.Add(At(obj)), id + " 与其他对象占用同一格 " + At(obj));
                Check(Floor(room, At(obj)), id + " 越界或位于墙内");
            }
            foreach (var box in room["boxes"]) Check(Floor(room, Cell(box["recoveryCell"])), "箱子恢复格无效");
        }

        public static IEnumerable<string> StructureIssues(JObject room)
        {
            var doors = room["doors"];
            string level = (string)room["levelId"];
            if (room["spawn"] == null) yield return $"{level} 需要放置玩家出生点";
            var recovery = new HashSet<Vector2Int>();
            foreach (var box in room["boxes"])
            {
                var cell = Cell(box["recoveryCell"]);
                if (!recovery.Add(cell)) yield return $"{level} {box["id"]} 恢复位置重复 {cell}";
            }
            foreach (var door in doors)
            {
                var cell = At(door);
                string direction = (string)door["direction"];
                if (direction == null || direction.Length != 1 || !"NESW".Contains(direction)
                    || Terrain(room, cell - Inward(door)) != Void || !Floor(room, cell + Inward(door)) || !Floor(room, cell))
                {
                    yield return $"{level} {cell} 门 {door["id"]} 需要朝外且内侧可通行";
                    continue;
                }
                if ((string)door["id"] == (string)room["primaryEntranceId"] &&
                    (Objects(room).Any(obj => At(obj) == cell + Inward(door)) || recovery.Contains(cell + Inward(door))))
                    yield return $"{level} {cell + Inward(door)} 主入口内侧需要留空";
            }
            if (!doors.Any(d => (string)d["id"] == (string)room["primaryEntranceId"] && (string)d["role"] == "entry"))
                yield return $"{level} 找不到主入口 {room["primaryEntranceId"]}";
            foreach (var issue in ObjectBehavior.Issues(room)) yield return issue;
            foreach (var cell in BoundaryGaps(room))
                yield return $"{level} {cell} 边界缺墙，请补墙或设置朝外的门";
        }

        public static IEnumerable<Vector2Int> BoundaryGaps(JObject room)
        {
            var size = Size(room);
            var openings = new HashSet<Vector2Int>(room["doors"].Select(At));
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    
                    var cell = new Vector2Int(x, y);
                    if (Floor(room, cell) && !openings.Contains(cell) &&
                        (Terrain(room, cell + Vector2Int.left) == Void || Terrain(room, cell + Vector2Int.right) == Void ||
                         Terrain(room, cell + Vector2Int.up) == Void || Terrain(room, cell + Vector2Int.down) == Void))
                        yield return cell;
                }
            }
        }

        public static void RequireStructure(JObject room)
        {
            string issue = StructureIssues(room).FirstOrDefault();
            Check(issue == null, issue);
        }
        public static void Write(string path, string contents, Action<string> validateTemporary = null)
        {
            path = Path.GetFullPath(path);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, contents);
                validateTemporary?.Invoke(temp);
                if (!File.Exists(path)) {File.Move(temp,path);return;}
                for(int attempt=0;;attempt++)
                {
                    try { File.Replace(temp,path,null); break; }
                    catch(IOException error) when (attempt<5 && ((error.HResult&65535)==1175 || (error.HResult&65535)==32))
                    { System.Threading.Thread.Sleep(30*(attempt+1)); }
                }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
