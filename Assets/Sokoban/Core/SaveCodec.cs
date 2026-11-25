/**
 * @file SaveCodec.cs
 * @brief 正式存档的数据、内容哈希与编解码：房内坐标加对象 ID，读档分无档、损坏、不兼容三态，保存前先试读。
 * SaveData：格式号、世界 ID、当前房间 ID、玩家的房内格、各房箱位（房内坐标，按箱 ID）与各房内容哈希。
 * ContentHash.Of(room)：只覆盖玩法字段（地形类别网格、对象类别与格子），不含外观、显示名与装饰，先规范化排序再取 SHA-256。
 * SaveStatus：Ok、NoSave、Corrupt、Incompatible 四种读档结果。
 * SaveLoad：读档结果状态与数据；Incompatible 与 Corrupt 带原因文字。
 * SaveCodec.ToJson(data)：用 MiniJson 把存档数据写成 JSON 文本，不做试读。
 * SaveCodec.Encode(data)：序列化为 JSON 文本，保存前调用 Decode 试读，失败则抛异常、不应写入文件。
 * SaveCodec.Decode(text, worldId, currentHashes)：文本为空为无档；解析或字段校验失败为损坏；格式号、世界 ID 或任一房哈希不符为不兼容。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Sokoban.Core
{
    public enum SaveStatus { Ok, NoSave, Corrupt, Incompatible }

    public sealed class SaveData
    {
        public const int CurrentFormat = 1;
        public int FormatVersion { get; set; } = CurrentFormat;
        public string WorldId { get; set; }
        public string RoomId { get; set; }
        public int PlayerX { get; set; }
        public int PlayerY { get; set; }
        public Dictionary<string, Dictionary<string, int[]>> Boxes { get; set; } = new Dictionary<string, Dictionary<string, int[]>>();
        public Dictionary<string, string> Hashes { get; set; } = new Dictionary<string, string>();
    }

    public sealed class SaveLoad
    {
        public SaveStatus Status { get; }
        public SaveData Data { get; }
        public string Reason { get; }

        public SaveLoad(SaveStatus status, SaveData data = null, string reason = null)
        {
            Status = status;
            Data = data;
            Reason = reason;
        }
    }

    public static class ContentHash
    {
        public static string Of(Room room)
        {
            var sb = new StringBuilder();
            sb.Append(room.Width).Append('x').Append(room.Height).Append('\n');
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                    sb.Append(room.TerrainAt(new Cell(x, y)).CategoryId).Append(',');
            sb.Append('\n');
            foreach (var o in room.Objects.OrderBy(o => o.Id, StringComparer.Ordinal))
                sb.Append(o.Id).Append(':').Append(o.CategoryId).Append('@').Append(o.Cell.X).Append(',').Append(o.Cell.Y).Append(';');
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
        }
    }

    public static class SaveCodec
    {
        public static string ToJson(SaveData data)
        {
            var boxes = new Dictionary<string, object>();
            foreach (var room in data.Boxes)
            {
                var cells = new Dictionary<string, object>();
                foreach (var box in room.Value)
                    cells[box.Key] = box.Value == null ? null : box.Value.Select(v => (object)(long)v).ToList();
                boxes[room.Key] = cells;
            }
            var hashes = new Dictionary<string, object>();
            foreach (var h in data.Hashes) hashes[h.Key] = h.Value;
            var root = new Dictionary<string, object>
            {
                ["formatVersion"] = (long)data.FormatVersion,
                ["worldId"] = data.WorldId,
                ["roomId"] = data.RoomId,
                ["playerX"] = (long)data.PlayerX,
                ["playerY"] = (long)data.PlayerY,
                ["boxes"] = boxes,
                ["hashes"] = hashes,
            };
            return MiniJson.Write(root);
        }

        public static string Encode(SaveData data)
        {
            string text = ToJson(data);
            var probe = Decode(text, data.WorldId, data.Hashes);
            if (probe.Status != SaveStatus.Ok) throw new InvalidOperationException("存档试读失败：" + probe.Reason);
            return text;
        }

        static string Str(Dictionary<string, object> d, string key) => d.TryGetValue(key, out var v) ? v as string : null;

        static int Int(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out var v) || !(v is long l) || l < int.MinValue || l > int.MaxValue)
                throw new FormatException("字段 " + key + " 不是有效整数");
            return (int)l;
        }

        static SaveData Read(string text)
        {
            if (!(MiniJson.Parse(text) is Dictionary<string, object> root)) throw new FormatException("存档根不是对象");
            var data = new SaveData
            {
                FormatVersion = Int(root, "formatVersion"),
                WorldId = Str(root, "worldId"),
                RoomId = Str(root, "roomId"),
                PlayerX = Int(root, "playerX"),
                PlayerY = Int(root, "playerY"),
            };
            if (!(root.TryGetValue("boxes", out var b) && b is Dictionary<string, object> boxes)) throw new FormatException("缺少 boxes");
            foreach (var room in boxes)
            {
                if (!(room.Value is Dictionary<string, object> cells)) throw new FormatException("箱位格式无效");
                var map = new Dictionary<string, int[]>();
                foreach (var cell in cells)
                {
                    if (!(cell.Value is List<object> pair) || pair.Count != 2 || !(pair[0] is long x) || !(pair[1] is long y))
                        throw new FormatException("箱位格式无效");
                    map[cell.Key] = new[] { (int)x, (int)y };
                }
                data.Boxes[room.Key] = map;
            }
            if (!(root.TryGetValue("hashes", out var h) && h is Dictionary<string, object> hashes)) throw new FormatException("缺少 hashes");
            foreach (var item in hashes)
            {
                if (!(item.Value is string hash)) throw new FormatException("哈希格式无效");
                data.Hashes[item.Key] = hash;
            }
            return data;
        }

        public static SaveLoad Decode(string text, string worldId, IReadOnlyDictionary<string, string> currentHashes)
        {
            if (string.IsNullOrWhiteSpace(text)) return new SaveLoad(SaveStatus.NoSave);
            SaveData data;
            try { data = Read(text); }
            catch (FormatException e) { return new SaveLoad(SaveStatus.Corrupt, null, e.Message); }
            if (data.WorldId == null || data.RoomId == null)
                return new SaveLoad(SaveStatus.Corrupt, null, "缺少必需字段");
            if (data.FormatVersion != SaveData.CurrentFormat)
                return new SaveLoad(SaveStatus.Incompatible, null, "格式号不符");
            if (data.WorldId != worldId) return new SaveLoad(SaveStatus.Incompatible, null, "世界 ID 不符");
            foreach (var h in data.Hashes)
                if (!currentHashes.TryGetValue(h.Key, out var now) || now != h.Value)
                    return new SaveLoad(SaveStatus.Incompatible, null, $"房间 {h.Key} 的内容已变化");
            if (!data.Hashes.ContainsKey(data.RoomId)) return new SaveLoad(SaveStatus.Corrupt, null, "当前房间没有哈希");
            return new SaveLoad(SaveStatus.Ok, data);
        }
    }
}
