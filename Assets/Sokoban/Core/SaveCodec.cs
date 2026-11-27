/**
 * @file SaveCodec.cs
 * @brief 正式存档的数据、内容哈希与编解码：房内坐标加对象 ID，规则状态与演出段分开保存，读档分无档、损坏、不兼容三态，保存前先试读。
 * SaveData：格式号、世界 ID、当前房间 ID、玩家的房内格及可选朝向、各房箱位（房内坐标，按箱 ID）、各房内容哈希；
 * 另含探索集合 Explored、道具背包 Items、SelectedItem、PendingItem（与探索集合是两份独立数据）、各房已解开过记录 EverSolved、对象状态 ObjectStates（房间到对象 ID 到状态）与演出段 Presentation。
 * PresentationSegment：演出段，存恢复进度 RecoveryT 与 RecoveryDuration、可选恢复项 RecoveryItemId、镜头朝向 Heading、进行中的移动种类 MotionKind、进度 MotionFraction、位移 MotionDelta 与是否推箱 MotionPushed，不进入规则层。
 * ContentHash.Of(draft)：只覆盖玩法字段（地形类别网格、对象的 ID 类别与格子含目标、恢复道具的脚本引用时长与参数、行为图含对象模板内的图），不含外观、显示名与装饰，先规范化排序再取 SHA-256。
 * SaveStatus：Ok、NoSave、Corrupt、Incompatible 四种读档结果。
 * SaveLoad：读档结果状态与数据；Incompatible 与 Corrupt 带原因文字。
 * SaveCodec.ToJson(data)：用 MiniJson 把存档数据写成 JSON 文本，不做试读；演出进度按千分之一取整数，进行中的段最多写为 999。
 * ProgressMilli、ReadProgress：转换归一化进度，旧版本四舍五入得到的 1000 按 999 恢复。
 * SaveCodec.Encode(data)：序列化为 JSON 文本，保存前调用 Decode 试读，失败抛 InvalidOperationException、不应写入文件。
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

    public sealed class PresentationSegment
    {
        public float RecoveryT { get; set; }
        public float RecoveryDuration { get; set; }
        public string RecoveryItemId { get; set; }
        public float Heading { get; set; }
        public string MotionKind { get; set; } = "none";
        public float MotionFraction { get; set; }
        public Cell MotionDelta { get; set; }
        public bool MotionPushed { get; set; }
    }

    public sealed class SaveData
    {
        public const int CurrentFormat = 1;
        public int FormatVersion { get; set; } = CurrentFormat;
        public string WorldId { get; set; }
        public string RoomId { get; set; }
        public int? Facing { get; set; }
        public int PlayerX { get; set; }
        public int PlayerY { get; set; }
        public Dictionary<string, Dictionary<string, int[]>> Boxes { get; set; } = new Dictionary<string, Dictionary<string, int[]>>();
        public Dictionary<string, string> Hashes { get; set; } = new Dictionary<string, string>();
        public HashSet<string> Explored { get; set; } = new HashSet<string>(StringComparer.Ordinal);
        public List<string> Items { get; set; } = new List<string>();
        public string SelectedItem { get; set; }
        public string PendingItem { get; set; }
        public Dictionary<string, bool> EverSolved { get; set; } = new Dictionary<string, bool>(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, string>> TerrainStates { get; } = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        public Dictionary<string, Dictionary<string, string>> ObjectStates { get; set; } = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        public PresentationSegment Presentation { get; set; } = new PresentationSegment();
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
        internal static long Milli(float value) => (long)Math.Round(value * 1000.0);

        public static string Of(RoomDraftModel draft)
        {
            var room = draft.Room;
            var sb = new StringBuilder();
            sb.Append(room.Width).Append('x').Append(room.Height).Append('\n');
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                    sb.Append(room.TerrainAt(new Cell(x, y)).CategoryId).Append(',');
            sb.Append('\n');
            foreach (var o in room.Objects.OrderBy(o => o.Id, StringComparer.Ordinal))
                sb.Append(o.Id).Append(':').Append(o.CategoryId).Append('@').Append(o.Cell.X).Append(',').Append(o.Cell.Y).Append(';');
            sb.Append('\n');
            foreach (var item in draft.RecoveryItems.OrderBy(i => i.Id, StringComparer.Ordinal))
            {
                sb.Append(item.Id).Append('|').Append(item.ScriptRef).Append('|').Append(Milli(item.Duration));
                foreach (var p in item.Params.OrderBy(p => p.Key, StringComparer.Ordinal)) sb.Append('|').Append(p.Key).Append('=').Append(Milli(p.Value));
                sb.Append(';');
            }
            sb.Append('\n');
            foreach (var g in draft.Graphs.OrderBy(g => g.Id, StringComparer.Ordinal)) AppendGraph(sb, g);
            sb.Append('\n');
            foreach (var t in draft.Templates.OrderBy(t => t.Key, StringComparer.Ordinal))
            {
                sb.Append(t.Key).Append('#');
                foreach (var g in t.Value.Graphs.OrderBy(g => g.Id, StringComparer.Ordinal)) AppendGraph(sb, g);
            }
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()))).Replace("-", "");
        }

        static void AppendGraph(StringBuilder sb, BehaviorGraph graph)
        {
            sb.Append('G').Append(graph.Id).Append('<').Append(graph.OwnerId).Append('>');
            foreach (var n in graph.Nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
            {
                sb.Append(n.Id).Append(':').Append(n.Kind);
                if (n.Cell.HasValue) sb.Append('@').Append(n.Cell.Value.X).Append(',').Append(n.Cell.Value.Y);
                foreach (var p in n.Parameters.OrderBy(p => p.Key, StringComparer.Ordinal)) sb.Append('{').Append(p.Key).Append('=').Append(p.Value).Append('}');
                sb.Append(';');
            }
            sb.Append('|');
            foreach (var e in graph.Edges.OrderBy(e => e.FromNode, StringComparer.Ordinal).ThenBy(e => e.FromPort, StringComparer.Ordinal)
                .ThenBy(e => e.ToNode, StringComparer.Ordinal).ThenBy(e => e.ToPort, StringComparer.Ordinal))
                sb.Append(e.FromNode).Append('.').Append(e.FromPort).Append('>').Append(e.ToNode).Append('.').Append(e.ToPort).Append(';');
            sb.Append('\n');
        }
    }

    public static class SaveCodec
    {
        static object Cells(int[] value) => value == null ? null : value.Select(v => (object)(long)v).ToList();

        static long ProgressMilli(float fraction)
        {
            if (float.IsNaN(fraction) || fraction < 0 || fraction > 1)
                throw new InvalidOperationException("进行中的演出进度须在 0 至 1 之间");
            return Math.Min(999, ContentHash.Milli(fraction));
        }

        public static string ToJson(SaveData data)
        {
            var boxes = new Dictionary<string, object>();
            foreach (var room in data.Boxes)
            {
                var cells = new Dictionary<string, object>();
                foreach (var box in room.Value) cells[box.Key] = Cells(box.Value);
                boxes[room.Key] = cells;
            }
            var hashes = new Dictionary<string, object>();
            foreach (var h in data.Hashes) hashes[h.Key] = h.Value;
            var solved = new Dictionary<string, object>();
            foreach (var s in data.EverSolved.OrderBy(s => s.Key, StringComparer.Ordinal)) solved[s.Key] = s.Value;
            var states = new Dictionary<string, object>();
            foreach (var room in data.ObjectStates.OrderBy(r => r.Key, StringComparer.Ordinal))
            {
                var map = new Dictionary<string, object>();
                foreach (var o in room.Value.OrderBy(o => o.Key, StringComparer.Ordinal)) map[o.Key] = o.Value;
                states[room.Key] = map;
            }
            var p = data.Presentation ?? new PresentationSegment();
            var presentation = new Dictionary<string, object>
            {
                ["recoveryT"] = ProgressMilli(p.RecoveryT),
                ["recoveryDuration"] = ContentHash.Milli(p.RecoveryDuration),
                ["heading"] = ContentHash.Milli(p.Heading),
                ["motionKind"] = p.MotionKind,
                ["motionFraction"] = ProgressMilli(p.MotionFraction),
                ["motionDx"] = (long)p.MotionDelta.X,
                ["motionDy"] = (long)p.MotionDelta.Y,
                ["motionPushed"] = p.MotionPushed,
            };
            if (p.RecoveryItemId != null) presentation["recoveryItemId"] = p.RecoveryItemId;
            var root = new Dictionary<string, object>
            {
                ["formatVersion"] = (long)data.FormatVersion,
                ["worldId"] = data.WorldId,
                ["roomId"] = data.RoomId,
                ["playerX"] = (long)data.PlayerX,
                ["playerY"] = (long)data.PlayerY,
                ["boxes"] = boxes,
                ["hashes"] = hashes,
                ["explored"] = data.Explored.OrderBy(r => r, StringComparer.Ordinal).Select(r => (object)r).ToList(),
                ["items"] = data.Items.Select(i => (object)i).ToList(),
                ["selectedItem"] = data.SelectedItem,
                ["pendingItem"] = data.PendingItem,
                ["everSolved"] = solved,
                ["objectStates"] = states,
                ["presentation"] = presentation,
            };
            if (data.TerrainStates.Count != 0)
                root["terrainStates"] = data.TerrainStates.ToDictionary(r => r.Key,
                    r => (object)r.Value.ToDictionary(gate => gate.Key, gate => (object)gate.Value));
            if (data.Facing.HasValue) root["facing"] = (long)data.Facing.Value;
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

        static float Float(Dictionary<string, object> d, string key) => Int(d, key) / 1000f;

        static float ReadProgress(Dictionary<string, object> data, string key)
        {
            int milli = Int(data, key);
            if (milli < 0 || milli > 1000) throw new FormatException("演出进度无效：" + key);
            return Math.Min(999, milli) / 1000f;
        }

        static bool Flag(Dictionary<string, object> d, string key)
        {
            if (d.TryGetValue(key, out var v) && v is bool b) return b;
            throw new FormatException("字段 " + key + " 不是布尔值");
        }

        static Dictionary<string, object> MapOf(Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out var v) && v is Dictionary<string, object> m ? m : throw new FormatException("缺少对象 " + key);

        static List<string> Strings(Dictionary<string, object> d, string key)
        {
            if (!(d.TryGetValue(key, out var v) && v is List<object> list)) throw new FormatException("缺少数组 " + key);
            return list.Select(i => i as string ?? throw new FormatException("字段 " + key + " 含非字符串项")).ToList();
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
                Facing = root.ContainsKey("facing") ? Int(root, "facing") : (int?)null,
            };
            if (data.Facing is int facing && (facing < 0 || facing > 3))
                throw new FormatException("玩家朝向档位必须为 0 到 3");
            foreach (var room in MapOf(root, "boxes"))
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
            foreach (var item in MapOf(root, "hashes"))
                data.Hashes[item.Key] = item.Value as string ?? throw new FormatException("哈希格式无效");

            var explored = Strings(root, "explored");
            ExplorationSet.Restore(explored);
            data.Explored = new HashSet<string>(explored, StringComparer.Ordinal);
            data.Items = Strings(root, "items");
            data.SelectedItem = Str(root, "selectedItem");
            data.PendingItem = Str(root, "pendingItem");
            ItemBag.Restore(data.Items, data.SelectedItem, data.PendingItem);
            foreach (var s in MapOf(root, "everSolved"))
                data.EverSolved[s.Key] = s.Value is bool b ? b : throw new FormatException("已解开记录不是布尔值");
            foreach (var room in MapOf(root, "objectStates"))
            {
                if (!(room.Value is Dictionary<string, object> objects)) throw new FormatException("对象状态格式无效");
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var o in objects) map[o.Key] = o.Value as string ?? throw new FormatException("对象状态不是字符串");
                data.ObjectStates[room.Key] = map;
            }

            if (root.ContainsKey("terrainStates"))
                foreach (var room in MapOf(root, "terrainStates"))
                {
                    if (!(room.Value is Dictionary<string, object> values)) throw new FormatException("地形状态格式无效");
                    var gates = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var gate in values)
                    {
                        if (!(gate.Value is string state) || state != "on" && state != "off" && state != "pending")
                            throw new FormatException("地形状态无效");
                        gates.Add(gate.Key, state);
                    }
                    data.TerrainStates.Add(room.Key, gates);
                }
            var p = MapOf(root, "presentation");
            var presentation = new PresentationSegment
            {
                RecoveryT = ReadProgress(p, "recoveryT"),
                RecoveryDuration = Float(p, "recoveryDuration"),
                RecoveryItemId = Str(p, "recoveryItemId"),
                Heading = Float(p, "heading"),
                MotionKind = Str(p, "motionKind") ?? throw new FormatException("缺少 motionKind"),
                MotionFraction = ReadProgress(p, "motionFraction"),
                MotionDelta = new Cell(Int(p, "motionDx"), Int(p, "motionDy")),
                MotionPushed = Flag(p, "motionPushed"),
            };
            if (p.ContainsKey("recoveryItemId") && (string.IsNullOrWhiteSpace(presentation.RecoveryItemId) || presentation.RecoveryDuration <= 0))
                throw new FormatException("恢复项记录无效");
            if (presentation.RecoveryDuration < 0 || presentation.RecoveryDuration == 0 && presentation.RecoveryT != 0)
                throw new FormatException("恢复进度无效");
            data.Presentation = presentation;
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
