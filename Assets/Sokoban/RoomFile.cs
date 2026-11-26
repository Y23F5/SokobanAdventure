/**
 * @file RoomFile.cs
 * @brief 独立房间格式与原子文件读写。ForExport 按分类外观目录和独立功能定义选择导出版本，Parse 读取及校验版本，Upgrade 按端点转换旧地形；
 * Check 验证必需条件，Cell 解出整数格，Point 编码格，Objects 枚举有位置的实体，
 * Size/At 分别读取尺寸及实体格，Inside/Floor 按北到南行序检查格子，Inward 计算门内侧位置，
 * Validate 检查字段、地形与身份；StructureIssues 定位边界缺口、门及入口预留错误。
 * ToLegacyData 把旧 JObject 房间抽成 Core 的 LegacyRoomData，供 Core 校验器与转换适配器使用；
 * ToLegacyData 还取出 Extras：Core 不认识的顶层键按键名存 JSON 文本，箱、目标、出生点、门、装饰物的整条原文按 box:、target:、spawn:、door:、decoration: 加 ID 存放，保证旧字段经 Core 往返不丢；
 * BoundaryGaps 经 LegacyRoomAdapter.ToDraft 与 Core 的 boundary.open 规则取边界缺口，ParseCoreCell 把规则定位文本还原成格坐标；
 * LegacyBoundaryGaps 保留旧的逐格判定，仅供新旧比对脚本对照；
 * DoorGeometryIssueIds 经 Core 的 door.geometry 规则取几何错误的门 ID，朝向字母非法时先由 StructureIssues 自行报错；
 * RequireStructure 在正式文件入口拒绝这些错误；Write 转调 Core 的 AtomicFile.Write，先写同目录临时文件再替换目标。
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
            var loadIssue = RoomCoreBridge.LoadIssues(room).FirstOrDefault();
            Check(loadIssue == null, loadIssue);
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
            if (Objects(room).Any(item => item["builtinModel"] != null && item.Parent != room["boxes"] || item["stateProfile"] != null) ||
                (room["terrainObjects"] as JArray)?.Any(item => item["builtinModel"] != null) == true) room["formatVersion"] = 3;
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
            string level = (string)room["levelId"];
            foreach (var issue in RoomCoreBridge.StructureIssues(room)) yield return issue;
            foreach (var issue in ObjectBehavior.Issues(room)) yield return issue;
            foreach (var issue in RoomDraft.DefinitionIssues(room)) yield return issue;
            foreach (var cell in BoundaryGaps(room))
                yield return $"{level} {cell} 边界缺墙，请补墙或设置朝外的门";
        }

        public static IEnumerable<string> LegacyStructureIssues(JObject room)
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
            var badGeometry = DoorGeometryIssueIds(room);
            foreach (var door in doors)
            {
                var cell = At(door);
                string direction = (string)door["direction"];
                if (direction == null || direction.Length != 1 || !"NESW".Contains(direction) || badGeometry.Contains((string)door["id"]))
                {
                    yield return $"{level} {cell} 门 {door["id"]} 需要朝外且内侧可通行";
                    continue;
                }
                if ((string)door["id"] == (string)room["primaryEntranceId"] &&
                    (Objects(room).Any(obj => !ReferenceEquals(obj, room["spawn"]) && At(obj) == cell + Inward(door)) || recovery.Contains(cell + Inward(door))))
                    yield return $"{level} {cell + Inward(door)} 主入口内侧需要留空";
            }
            if (!doors.Any(d => (string)d["id"] == (string)room["primaryEntranceId"] && (string)d["role"] == "entry"))
                yield return $"{level} 找不到主入口 {room["primaryEntranceId"]}";
            foreach (var issue in ObjectBehavior.Issues(room)) yield return issue;
            foreach (var issue in RoomDraft.DefinitionIssues(room)) yield return issue;
            foreach (var cell in BoundaryGaps(room))
                yield return $"{level} {cell} 边界缺墙，请补墙或设置朝外的门";
        }

        static HashSet<string> DoorGeometryIssueIds(JObject room)
        {
            bool badDirection = room["doors"].Any(door =>
            {
                string direction = (string)door["direction"];
                return direction == null || direction.Length != 1 || !"NESW".Contains(direction);
            });
            if (badDirection) return new HashSet<string>();
            var draft = Sokoban.Core.LegacyRoomAdapter.ToDraft(ToLegacyData(room));
            return new HashSet<string>(Sokoban.Core.RoomValidator.ValidateDraft(draft, Sokoban.Core.ValidationStage.Publish)
                .Where(issue => issue.RuleId == "door.geometry")
                .Select(issue => issue.Location));
        }

        static Sokoban.Core.Cell ToCore(Vector2Int cell) => new Sokoban.Core.Cell(cell.x, cell.y);

        public static Sokoban.Core.LegacyRoomData ToLegacyData(JObject room)
        {
            var size = Size(room);
            Sokoban.Core.LegacyEntity Entity(JToken token) =>
                token == null ? null : new Sokoban.Core.LegacyEntity((string)token["id"], ToCore(At(token)));
            return new Sokoban.Core.LegacyRoomData
            {
                LevelId = (string)room["levelId"],
                Width = size.x,
                Height = size.y,
                RowsNorthToSouth = ((JArray)room["terrainRowsNorthToSouth"]).Select(row => (string)row).ToArray(),
                Doors = room["doors"].Select(door => new Sokoban.Core.LegacyDoor((string)door["id"], ToCore(At(door)), (string)door["direction"], (string)door["role"])).ToArray(),
                PrimaryEntranceId = (string)room["primaryEntranceId"],
                Boxes = room["boxes"].Select(Entity).ToArray(),
                Targets = room["targets"].Select(Entity).ToArray(),
                Spawn = Entity(room["spawn"]),
                Extras = ExtrasOf(room),
            };
        }

        static readonly string[] StructuralKeys = { "levelId", "size", "terrainRowsNorthToSouth", "doors", "primaryEntranceId", "boxes", "targets", "spawn", "decorations", "formatVersion", "kind" };

        static Dictionary<string, string> ExtrasOf(JObject room)
        {
            var extras = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in room.Properties().Where(p => !StructuralKeys.Contains(p.Name)))
                extras[property.Name] = property.Value.ToString(Formatting.None);
            foreach (var item in room["decorations"] as JArray ?? new JArray())
                extras["decoration:" + (string)item["id"]] = item.ToString(Formatting.None);
            foreach (var item in room["boxes"])
                extras["box:" + (string)item["id"]] = item.ToString(Formatting.None);
            foreach (var item in room["targets"])
                extras["target:" + (string)item["id"]] = item.ToString(Formatting.None);
            if (room["spawn"] != null) extras["spawn:" + (string)room["spawn"]["id"]] = room["spawn"].ToString(Formatting.None);
            foreach (var item in room["doors"])
                extras["door:" + (string)item["id"]] = item.ToString(Formatting.None);
            return extras;
        }

        public static IEnumerable<Vector2Int> BoundaryGaps(JObject room)
        {
            var draft = Sokoban.Core.LegacyRoomAdapter.ToDraft(ToLegacyData(room));
            return Sokoban.Core.RoomValidator.ValidateDraft(draft, Sokoban.Core.ValidationStage.Publish)
                .Where(issue => issue.RuleId == "boundary.open")
                .Select(issue => ParseCoreCell(issue.Location))
                .ToList();
        }

        static Vector2Int ParseCoreCell(string text)
        {
            var parts = text.Trim('(', ')').Split(',');
            return new Vector2Int(int.Parse(parts[0]), int.Parse(parts[1]));
        }

        public static IEnumerable<Vector2Int> LegacyBoundaryGaps(JObject room)
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
        public static void Write(string path, string contents, Action<string> validateTemporary = null) =>
            Sokoban.Core.AtomicFile.Write(path, contents, validateTemporary);
    }
}
