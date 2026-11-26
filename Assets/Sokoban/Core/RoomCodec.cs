/**
 * @file RoomCodec.cs
 * @brief 房间文件的数据与编解码：类型化地形网格、对象、门、主入口与恢复道具，用 MiniJson 读写，不依赖外部库。
 * RecoveryItem：恢复道具（道具 ID、脚本引用、时长秒、数值参数表），ID 与房内对象共用同一份 ID 空间。
 * RoomDocument：房间文件的内存形态，含 Room、门列表、主入口门 ID（可为空）与恢复道具列表。
 * 对象记录可带可选的 recoveryX、recoveryY（箱子恢复格），缺省表示没有；
 * RoomCodec.Encode(doc)：写成 JSON 文本；小数不进 JSON，时长与参数按千分之一取整数；写完先调用 Decode 试读，失败抛 InvalidOperationException，调用方不应写入文件。
 * RoomCodec.Decode(text)：解析并校验格式号、尺寸、地形类别、对象类别、格子范围、ID 唯一、时长为正与主入口引用，任何问题统一抛 FormatException。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class RecoveryItem
    {
        public string Id { get; }
        public string ScriptRef { get; }
        public float Duration { get; }
        public IReadOnlyDictionary<string, float> Params { get; }

        public RecoveryItem(string id, string scriptRef, float duration, IReadOnlyDictionary<string, float> parameters = null)
        {
            Id = id;
            ScriptRef = scriptRef;
            Duration = duration;
            Params = parameters ?? new Dictionary<string, float>();
        }
    }

    public sealed class RoomDocument
    {
        public Room Room { get; }
        public IReadOnlyList<DoorInfo> Doors { get; }
        public string PrimaryEntranceId { get; }
        public IReadOnlyList<RecoveryItem> RecoveryItems { get; }

        public RoomDocument(Room room, IReadOnlyList<DoorInfo> doors, string primaryEntranceId, IReadOnlyList<RecoveryItem> recoveryItems)
        {
            Room = room;
            Doors = doors ?? new List<DoorInfo>();
            PrimaryEntranceId = primaryEntranceId;
            RecoveryItems = recoveryItems ?? new List<RecoveryItem>();
        }
    }

    public static class RoomCodec
    {
        public const int FormatVersion = 1;
        public const int MaxSize = 256;
        const string RecoveryKind = "recovery";

        static readonly HashSet<string> TerrainIds = new HashSet<string>
        {
            TerrainCategories.Void, TerrainCategories.Floor, TerrainCategories.Wall, TerrainCategories.Door, TerrainCategories.Openable
        };

        static readonly HashSet<string> ObjectIds = new HashSet<string>
        {
            ObjectCategories.Box, ObjectCategories.Target, ObjectCategories.Spawn, ObjectCategories.Custom
        };

        public static string Encode(RoomDocument doc)
        {
            var room = doc.Room;
            var terrain = new List<object>();
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                {
                    var t = room.TerrainAt(new Cell(x, y));
                    var rec = new Dictionary<string, object> { ["category"] = t.CategoryId };
                    if (t.EntityId != null) rec["entity"] = t.EntityId;
                    if (t.State != null) rec["state"] = t.State;
                    terrain.Add(rec);
                }
            var objects = room.Objects.Select(o => (object)new Dictionary<string, object>
            {
                ["id"] = o.Id, ["category"] = o.CategoryId, ["x"] = (long)o.Cell.X, ["y"] = (long)o.Cell.Y
            }).ToList();
            foreach (var (o, rec) in room.Objects.Zip(objects, (o, rec) => (o, (Dictionary<string, object>)rec)))
                if (o.RecoveryCell.HasValue) { rec["recoveryX"] = (long)o.RecoveryCell.Value.X; rec["recoveryY"] = (long)o.RecoveryCell.Value.Y; }
            var doors = doc.Doors.Select(d => (object)new Dictionary<string, object>
            {
                ["id"] = d.Id, ["x"] = (long)d.Cell.X, ["y"] = (long)d.Cell.Y,
                ["outX"] = (long)d.Outward.X, ["outY"] = (long)d.Outward.Y, ["role"] = d.Role
            }).ToList();
            var items = doc.RecoveryItems.Select(i =>
            {
                var parameters = new Dictionary<string, object>();
                foreach (var p in i.Params.OrderBy(p => p.Key, StringComparer.Ordinal)) parameters[p.Key] = Milli(p.Value);
                return (object)new Dictionary<string, object>
                {
                    ["id"] = i.Id, ["script"] = i.ScriptRef, ["duration"] = Milli(i.Duration), ["params"] = parameters
                };
            }).ToList();
            var root = new Dictionary<string, object>
            {
                ["formatVersion"] = (long)FormatVersion,
                ["kind"] = "room",
                ["id"] = room.Id,
                ["width"] = (long)room.Width,
                ["height"] = (long)room.Height,
                ["terrain"] = terrain,
                ["objects"] = objects,
                ["doors"] = doors,
                ["primaryEntranceId"] = doc.PrimaryEntranceId,
                ["recovery"] = items,
            };
            string text = MiniJson.Write(root);
            try { Decode(text); }
            catch (FormatException e) { throw new InvalidOperationException("房间文件试读失败：" + e.Message, e); }
            return text;
        }

        public static RoomDocument Decode(string text)
        {
            if (!(MiniJson.Parse(text) is Dictionary<string, object> root)) throw new FormatException("房间文件根不是对象");
            try { return Build(root); }
            catch (ArgumentException e) { throw new FormatException(e.Message, e); }
        }

        static long Milli(float value) => (long)Math.Round(value * 1000.0);

        static Dictionary<string, object> Obj(object v, string what) =>
            v as Dictionary<string, object> ?? throw new FormatException(what + "不是对象");

        static List<object> Arr(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v is List<object> list ? list : throw new FormatException("缺少数组 " + key);

        static string Str(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v is string s ? s : throw new FormatException("缺少字符串 " + key);

        static string OptStr(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out var v) || v == null) return null;
            return v as string ?? throw new FormatException("字段 " + key + " 不是字符串");
        }

        static int Int(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out var v) || !(v is long l) || l < int.MinValue || l > int.MaxValue)
                throw new FormatException("字段 " + key + " 不是有效整数");
            return (int)l;
        }

        static Cell CellOf(Room room, Dictionary<string, object> d, string what)
        {
            var cell = new Cell(Int(d, "x"), Int(d, "y"));
            if (!room.Inside(cell)) throw new FormatException(what + " 的格子 " + cell + " 超出房间");
            return cell;
        }

        static RoomDocument Build(Dictionary<string, object> root)
        {
            if (Int(root, "formatVersion") != FormatVersion) throw new FormatException("房间文件格式号不符");
            if (Str(root, "kind") != "room") throw new FormatException("文件不是房间");
            int width = Int(root, "width"), height = Int(root, "height");
            if (width <= 0 || height <= 0 || width > MaxSize || height > MaxSize) throw new FormatException("房间尺寸无效");
            var room = new Room(Str(root, "id"), width, height);

            var terrain = Arr(root, "terrain");
            if (terrain.Count != width * height) throw new FormatException("地形记录数与尺寸不符");
            for (int i = 0; i < terrain.Count; i++)
            {
                var rec = Obj(terrain[i], "地形记录");
                string category = Str(rec, "category");
                if (!TerrainIds.Contains(category)) throw new FormatException("未知地形类别 " + category);
                if (category == TerrainCategories.Void) continue;
                room.SetTerrain(new Cell(i % width, i / width), category, OptStr(rec, "entity"), OptStr(rec, "state"));
            }

            foreach (var item in Arr(root, "objects"))
            {
                var rec = Obj(item, "对象记录");
                string category = Str(rec, "category");
                if (!ObjectIds.Contains(category)) throw new FormatException("未知对象类别 " + category);
                Cell? recoveryCell = null;
                if (rec.ContainsKey("recoveryX") || rec.ContainsKey("recoveryY"))
                {
                    recoveryCell = new Cell(Int(rec, "recoveryX"), Int(rec, "recoveryY"));
                    if (!room.Inside(recoveryCell.Value)) throw new FormatException("对象恢复格 " + recoveryCell.Value + " 超出房间");
                }
                room.AddObject(Str(rec, "id"), category, CellOf(room, rec, "对象"), recoveryCell);
            }

            var doors = new List<DoorInfo>();
            foreach (var item in Arr(root, "doors"))
            {
                var rec = Obj(item, "门记录");
                doors.Add(new DoorInfo(Str(rec, "id"), CellOf(room, rec, "门"), new Cell(Int(rec, "outX"), Int(rec, "outY")), Str(rec, "role")));
            }

            var recovery = new List<RecoveryItem>();
            foreach (var item in Arr(root, "recovery"))
            {
                var rec = Obj(item, "恢复道具记录");
                string id = Str(rec, "id");
                int duration = Int(rec, "duration");
                if (duration <= 0) throw new FormatException("恢复道具 " + id + " 的时长必须大于 0");
                var parameters = new Dictionary<string, float>(StringComparer.Ordinal);
                if (!rec.TryGetValue("params", out var raw) || !(raw is Dictionary<string, object> table)) throw new FormatException("缺少 params");
                foreach (var p in table) parameters[p.Key] = Int(table, p.Key) / 1000f;
                room.Ids.Add(id, RecoveryKind);
                recovery.Add(new RecoveryItem(id, Str(rec, "script"), duration / 1000f, parameters));
            }

            string primary = OptStr(root, "primaryEntranceId");
            if (primary != null && !doors.Any(d => d.Id == primary && d.Role == "entry"))
                throw new FormatException("主入口 " + primary + " 不是入口门");
            return new RoomDocument(room, doors, primary, recovery);
        }
    }
}
