/**
 * @file WorldCodec.cs
 * @brief 世界文件的数据与编解码：世界 ID、内容版本、有限整数边界、房间引用与原点、门连接、起点与终点，用 MiniJson 读写。
 * WorldEndpoint：一个端点，由房间 ID 与门 ID（起点时为出生点 ID）组成。
 * WorldRoomRef：世界中的一个房间引用（房间 ID、相对世界文件的文件名、整数原点、前进所需已解开的房间 ID 列表）。
 * WorldConnection：一条门连接（唯一 ID、模式 direct 或 teleport、源端点、目标端点、是否允许回退）。
 * WorldDocument：世界文件的内存形态，含边界、房间引用、连接、起点、终点端点与终点所需已解开的房间。
 * 起点按 levelId 加 spawnId 读写（读入时也接受 doorId）；WorldCodec.Encode(doc)：写成 JSON 文本（allowReturn 写为布尔值，读入时布尔与 0、1 都接受），写完先调用 Decode 试读，失败抛 InvalidOperationException，调用方不应写入文件。
 * WorldCodec.Decode(text)：校验种类、格式号、边界方向、房间 ID 唯一、连接 ID 唯一、模式合法、连接与起终点引用的房间存在、前置房间存在，任何问题统一抛 FormatException。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class WorldEndpoint
    {
        public string LevelId { get; }
        public string DoorId { get; }

        public WorldEndpoint(string levelId, string doorId)
        {
            LevelId = levelId;
            DoorId = doorId;
        }
    }

    public sealed class WorldRoomRef
    {
        public string LevelId { get; }
        public string File { get; }
        public Cell Origin { get; }
        public IReadOnlyList<string> ForwardRequiresSolved { get; }

        public WorldRoomRef(string levelId, string file, Cell origin, IReadOnlyList<string> forwardRequiresSolved)
        {
            LevelId = levelId;
            File = file;
            Origin = origin;
            ForwardRequiresSolved = forwardRequiresSolved ?? new string[0];
        }
    }

    public sealed class WorldConnection
    {
        public string Id { get; }
        public string Mode { get; }
        public WorldEndpoint From { get; }
        public WorldEndpoint To { get; }
        public bool AllowReturn { get; }

        public WorldConnection(string id, string mode, WorldEndpoint from, WorldEndpoint to, bool allowReturn)
        {
            Id = id;
            Mode = mode;
            From = from;
            To = to;
            AllowReturn = allowReturn;
        }
    }

    public sealed class WorldDocument
    {
        public string WorldId { get; }
        public int ContentVersion { get; }
        public Cell Min { get; }
        public Cell Max { get; }
        public IReadOnlyList<WorldRoomRef> Rooms { get; }
        public IReadOnlyList<WorldConnection> Connections { get; }
        public WorldEndpoint Start { get; }
        public WorldEndpoint FinishEndpoint { get; }
        public IReadOnlyList<string> FinishRequiresSolved { get; }

        public WorldDocument(string worldId, int contentVersion, Cell min, Cell max,
            IReadOnlyList<WorldRoomRef> rooms, IReadOnlyList<WorldConnection> connections,
            WorldEndpoint start, WorldEndpoint finishEndpoint, IReadOnlyList<string> finishRequiresSolved)
        {
            WorldId = worldId;
            ContentVersion = contentVersion;
            Min = min;
            Max = max;
            Rooms = rooms ?? new List<WorldRoomRef>();
            Connections = connections ?? new List<WorldConnection>();
            Start = start;
            FinishEndpoint = finishEndpoint;
            FinishRequiresSolved = finishRequiresSolved ?? new string[0];
        }
    }

    public static class WorldCodec
    {
        public const int FormatVersion = 1;
        public const string Direct = "direct";
        public const string Teleport = "teleport";

        public static string Encode(WorldDocument doc)
        {
            Func<Cell, object> pair = c => new List<object> { (long)c.X, (long)c.Y };
            Func<WorldEndpoint, object> endpoint = e => e == null ? null : new Dictionary<string, object>
            {
                ["levelId"] = e.LevelId, ["doorId"] = e.DoorId
            };
            var rooms = doc.Rooms.Select(r => (object)new Dictionary<string, object>
            {
                ["levelId"] = r.LevelId,
                ["file"] = r.File,
                ["origin"] = pair(r.Origin),
                ["forwardRequiresSolved"] = r.ForwardRequiresSolved.Select(s => (object)s).ToList(),
            }).ToList();
            var connections = doc.Connections.Select(c => (object)new Dictionary<string, object>
            {
                ["id"] = c.Id,
                ["mode"] = c.Mode,
                ["forwardFrom"] = endpoint(c.From),
                ["forwardTo"] = endpoint(c.To),
                ["allowReturn"] = c.AllowReturn,
            }).ToList();
            var root = new Dictionary<string, object>
            {
                ["kind"] = "world",
                ["formatVersion"] = (long)FormatVersion,
                ["worldId"] = doc.WorldId,
                ["contentVersion"] = (long)doc.ContentVersion,
                ["bounds"] = new Dictionary<string, object> { ["min"] = pair(doc.Min), ["max"] = pair(doc.Max) },
                ["rooms"] = rooms,
                ["connections"] = connections,
                ["start"] = new Dictionary<string, object> { ["levelId"] = doc.Start.LevelId, ["spawnId"] = doc.Start.DoorId },
                ["finish"] = new Dictionary<string, object>
                {
                    ["endpoint"] = endpoint(doc.FinishEndpoint),
                    ["requiresSolved"] = doc.FinishRequiresSolved.Select(s => (object)s).ToList(),
                },
            };
            string text = MiniJson.Write(root);
            try { Decode(text); }
            catch (FormatException e) { throw new InvalidOperationException("世界文件试读失败：" + e.Message, e); }
            return text;
        }

        public static WorldDocument Decode(string text)
        {
            if (!(MiniJson.Parse(text) is Dictionary<string, object> root)) throw new FormatException("世界文件根不是对象");
            return Build(root);
        }

        static Dictionary<string, object> Obj(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v is Dictionary<string, object> o ? o : throw new FormatException("缺少对象 " + key);

        static List<object> Arr(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v is List<object> a ? a : throw new FormatException("缺少数组 " + key);

        static bool Flag(Dictionary<string, object> d, string key)
        {
            if (d.TryGetValue(key, out var v))
            {
                if (v is bool b) return b;
                if (v is long l && (l == 0 || l == 1)) return l == 1;
            }
            throw new FormatException("字段 " + key + " 不是布尔值");
        }

        static string Str(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v is string s ? s : throw new FormatException("缺少字符串 " + key);

        static int Int(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out var v) || !(v is long l) || l < int.MinValue || l > int.MaxValue)
                throw new FormatException("字段 " + key + " 不是有效整数");
            return (int)l;
        }

        static Cell PairOf(Dictionary<string, object> d, string key)
        {
            var a = Arr(d, key);
            if (a.Count != 2 || !(a[0] is long x) || !(a[1] is long y) || x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
                throw new FormatException("字段 " + key + " 不是整数对");
            return new Cell((int)x, (int)y);
        }

        static List<string> Strings(Dictionary<string, object> d, string key)
        {
            var list = new List<string>();
            foreach (var item in Arr(d, key))
                list.Add(item as string ?? throw new FormatException("字段 " + key + " 含非字符串项"));
            return list;
        }

        static WorldEndpoint EndpointOf(Dictionary<string, object> d, string key)
        {
            var o = Obj(d, key);
            return new WorldEndpoint(Str(o, "levelId"), Str(o, "doorId"));
        }

        static WorldEndpoint StartOf(Dictionary<string, object> root)
        {
            var o = Obj(root, "start");
            string id = o.TryGetValue("spawnId", out var s) && s is string spawn ? spawn
                : o.TryGetValue("doorId", out var d) && d is string door ? door
                : throw new FormatException("起点缺少 spawnId");
            return new WorldEndpoint(Str(o, "levelId"), id);
        }

        static WorldDocument Build(Dictionary<string, object> root)
        {
            if (Str(root, "kind") != "world") throw new FormatException("文件不是世界");
            if (Int(root, "formatVersion") != FormatVersion) throw new FormatException("世界文件格式号不符");
            var bounds = Obj(root, "bounds");
            var min = PairOf(bounds, "min");
            var max = PairOf(bounds, "max");
            if (max.X < min.X || max.Y < min.Y) throw new FormatException("世界边界方向相反");

            var rooms = new List<WorldRoomRef>();
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Arr(root, "rooms"))
            {
                if (!(item is Dictionary<string, object> r)) throw new FormatException("房间引用不是对象");
                string id = Str(r, "levelId");
                if (!known.Add(id)) throw new FormatException("房间 ID 重复：" + id);
                rooms.Add(new WorldRoomRef(id, Str(r, "file"), PairOf(r, "origin"), Strings(r, "forwardRequiresSolved")));
            }
            foreach (var r in rooms)
                foreach (var need in r.ForwardRequiresSolved)
                    if (!known.Contains(need)) throw new FormatException("房间 " + r.LevelId + " 的前置房间不存在：" + need);

            var connections = new List<WorldConnection>();
            var linkIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Arr(root, "connections"))
            {
                if (!(item is Dictionary<string, object> c)) throw new FormatException("连接不是对象");
                string id = Str(c, "id");
                if (!linkIds.Add(id)) throw new FormatException("连接 ID 重复：" + id);
                string mode = Str(c, "mode");
                if (mode != Direct && mode != Teleport) throw new FormatException("未知连接模式 " + mode);
                var from = EndpointOf(c, "forwardFrom");
                var to = EndpointOf(c, "forwardTo");
                if (!known.Contains(from.LevelId) || !known.Contains(to.LevelId)) throw new FormatException("连接 " + id + " 引用了不存在的房间");
                connections.Add(new WorldConnection(id, mode, from, to, Flag(c, "allowReturn")));
            }

            var start = StartOf(root);
            if (!known.Contains(start.LevelId)) throw new FormatException("起点引用了不存在的房间");
            var finish = Obj(root, "finish");
            var finishEndpoint = EndpointOf(finish, "endpoint");
            if (!known.Contains(finishEndpoint.LevelId)) throw new FormatException("终点引用了不存在的房间");
            var finishRequires = Strings(finish, "requiresSolved");
            foreach (var need in finishRequires)
                if (!known.Contains(need)) throw new FormatException("终点所需房间不存在：" + need);

            return new WorldDocument(Str(root, "worldId"), Int(root, "contentVersion"), min, max, rooms, connections, start, finishEndpoint, finishRequires);
        }
    }
}
