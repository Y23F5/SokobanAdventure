/**
 * @file RoomCodec.cs
 * @brief 房间文件的编解码：把 RoomDraftModel 的全部可保存字段写成 JSON 并读回，用 MiniJson，不依赖外部库。
 * RecoveryItem：恢复道具（道具 ID、脚本引用、时长秒、数值参数表），ID 与房内对象共用同一份 ID 空间。
 * RoomCodec.Encode(draft)：写出房间名与主题、类型化地形网格（类别、实体 ID、状态、外观引用与变体序号）、对象及其外观、门与主入口、恢复道具、装饰、资源描述符、行为图（节点带格坐标）、对象模板、模板实例、对象状态表、界面图资源 ID 与 Extras；
 * 小数不进 JSON，恢复道具时长与参数按千分之一取整数；写完先调用 Decode 试读，任何问题抛 FormatException，调用方不应写入文件。
 * RoomCodec.Decode(text)：返回 RoomDraftModel；校验格式号、尺寸、地形与对象类别（取自注册表）、格子范围、ID 在房间内唯一、恢复时长为正、主入口引用与各字段类型，任何问题统一抛 FormatException。
 * GraphOut、StatesOut、GraphIn、StatesIn：房间文件与对象模板共用同一份行为图与对象状态表写法。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class RecoveryItem
    {
        public const float DefaultDuration = 3.7f;

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

    public static class RoomCodec
    {
        public const int FormatVersion = 1;
        public const int MaxSize = 256;
        const string RecoveryKind = "recovery";

        public static string Encode(RoomDraftModel draft)
        {
            string text = MiniJson.Write(ToRoot(draft));
            try { Decode(text); }
            catch (FormatException e) { throw new FormatException("房间文件试读失败：" + e.Message, e); }
            return text;
        }

        static long Milli(float value) => (long)Math.Round(value * 1000.0);

        static void PutAppearance(Dictionary<string, object> rec, AppearanceRef appearance)
        {
            if (appearance == null) return;
            rec["appearance"] = appearance.Id;
            rec["variant"] = (long)appearance.Variant;
        }

        static Dictionary<string, object> StringMap(IReadOnlyDictionary<string, string> map)
        {
            var result = new Dictionary<string, object>();
            foreach (var p in map.OrderBy(p => p.Key, StringComparer.Ordinal)) result[p.Key] = p.Value;
            return result;
        }

        internal static object GraphOut(BehaviorGraph g)
        {
            var nodes = g.Nodes.Select(n =>
            {
                var rec = new Dictionary<string, object> { ["id"] = n.Id, ["kind"] = n.Kind, ["params"] = StringMap(n.Parameters) };
                if (n.Cell.HasValue) { rec["x"] = (long)n.Cell.Value.X; rec["y"] = (long)n.Cell.Value.Y; }
                return (object)rec;
            }).ToList();
            var edges = g.Edges.Select(e => (object)new Dictionary<string, object>
            {
                ["fromNode"] = e.FromNode, ["fromPort"] = e.FromPort, ["toNode"] = e.ToNode, ["toPort"] = e.ToPort
            }).ToList();
            return new Dictionary<string, object> { ["id"] = g.Id, ["owner"] = g.OwnerId, ["nodes"] = nodes, ["edges"] = edges };
        }

        internal static object StatesOut(ObjectStateTable table)
        {
            if (table == null) return null;
            var transitions = table.Transitions.Select(t =>
            {
                var rec = new Dictionary<string, object> { ["from"] = t.From, ["trigger"] = t.Trigger, ["to"] = t.To };
                PutAppearance(rec, t.Remnant);
                return (object)rec;
            }).ToList();
            return new Dictionary<string, object>
            {
                ["initial"] = table.Initial,
                ["states"] = table.States.Select(s => (object)s).ToList(),
                ["transitions"] = transitions,
            };
        }

        static Dictionary<string, object> ToRoot(RoomDraftModel draft)
        {
            var room = draft.Room;
            var terrain = new List<object>();
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                {
                    var cell = new Cell(x, y);
                    var t = room.TerrainAt(cell);
                    var rec = new Dictionary<string, object> { ["category"] = t.CategoryId };
                    if (t.EntityId != null) rec["entity"] = t.EntityId;
                    if (t.State != null) rec["state"] = t.State;
                    PutAppearance(rec, room.AppearanceAt(cell));
                    terrain.Add(rec);
                }
            var objects = room.Objects.Select(o =>
            {
                var rec = new Dictionary<string, object> { ["id"] = o.Id, ["category"] = o.CategoryId, ["x"] = (long)o.Cell.X, ["y"] = (long)o.Cell.Y };
                PutAppearance(rec, o.Appearance);
                return (object)rec;
            }).ToList();
            var doors = draft.Doors.Select(d => (object)new Dictionary<string, object>
            {
                ["id"] = d.Id, ["x"] = (long)d.Cell.X, ["y"] = (long)d.Cell.Y,
                ["outX"] = (long)d.Outward.X, ["outY"] = (long)d.Outward.Y, ["role"] = d.Role
            }).ToList();
            var items = draft.RecoveryItems.Select(i =>
            {
                var parameters = new Dictionary<string, object>();
                foreach (var p in i.Params.OrderBy(p => p.Key, StringComparer.Ordinal)) parameters[p.Key] = Milli(p.Value);
                return (object)new Dictionary<string, object>
                {
                    ["id"] = i.Id, ["script"] = i.ScriptRef, ["duration"] = Milli(i.Duration), ["params"] = parameters
                };
            }).ToList();
            var decorations = draft.Decorations.Select(d =>
            {
                var rec = new Dictionary<string, object> { ["id"] = d.Id, ["x"] = (long)d.Cell.X, ["y"] = (long)d.Cell.Y };
                PutAppearance(rec, d.Appearance);
                return (object)rec;
            }).ToList();
            var assets = draft.Assets.Select(a => (object)new Dictionary<string, object>
            {
                ["id"] = a.Id, ["kind"] = a.Kind, ["name"] = a.Name, ["sha256"] = a.Sha256, ["extension"] = a.Extension
            }).ToList();
            var graphs = draft.Graphs.Select(g => GraphOut(g)).ToList();
            var templates = draft.Templates.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p =>
            {
                var rec = new Dictionary<string, object>
                {
                    ["id"] = p.Key, ["name"] = p.Value.Name,
                    ["graphs"] = p.Value.Graphs.Select(g => GraphOut(g)).ToList(),
                    ["states"] = StatesOut(p.Value.States),
                };
                PutAppearance(rec, p.Value.Appearance);
                return (object)rec;
            }).ToList();
            var instances = draft.Instances.Select(i => (object)new Dictionary<string, object>
            {
                ["id"] = i.Id, ["templateId"] = i.TemplateId, ["overrides"] = StringMap(i.Overrides)
            }).ToList();
            var objectStates = new Dictionary<string, object>();
            foreach (var p in draft.ObjectStates.OrderBy(p => p.Key, StringComparer.Ordinal)) objectStates[p.Key] = StatesOut(p.Value);
            var root = new Dictionary<string, object>
            {
                ["formatVersion"] = (long)FormatVersion,
                ["kind"] = "room",
                ["id"] = room.Id,
                ["name"] = draft.Meta.Name,
                ["themeId"] = draft.Meta.ThemeId,
                ["width"] = (long)room.Width,
                ["height"] = (long)room.Height,
                ["terrain"] = terrain,
                ["objects"] = objects,
                ["doors"] = doors,
                ["primaryEntranceId"] = draft.PrimaryEntranceId,
                ["recovery"] = items,
                ["decorations"] = decorations,
                ["assets"] = assets,
                ["graphs"] = graphs,
                ["templates"] = templates,
                ["instances"] = instances,
                ["objectStates"] = objectStates,
                ["uiImage"] = draft.UiImageAssetId,
            };
            if (draft.Extras.Count > 0)
            {
                var extras = new Dictionary<string, object>();
                foreach (var pair in draft.Extras) extras[pair.Key] = pair.Value;
                root["extras"] = extras;
            }
            return root;
        }

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

        public static RoomDraftModel Decode(string text)
        {
            if (text == null) throw new FormatException("房间文件为空");
            if (!(MiniJson.Parse(text) is Dictionary<string, object> root)) throw new FormatException("房间文件根不是对象");
            try { return Build(root); }
            catch (ArgumentException e) { throw new FormatException(e.Message, e); }
            catch (InvalidOperationException e) { throw new FormatException(e.Message, e); }
        }

        static AppearanceRef AppearanceOf(Dictionary<string, object> rec)
        {
            string id = OptStr(rec, "appearance");
            if (id == null) return null;
            return new AppearanceRef(id, rec.ContainsKey("variant") ? Int(rec, "variant") : 0);
        }

        static RoomDraftModel Build(Dictionary<string, object> root)
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
                if (!Registries.Terrains.Contains(category)) throw new FormatException("未知地形类别 " + category);
                var cell = new Cell(i % width, i / width);
                if (category != TerrainCategories.Void) room.SetTerrain(cell, category, OptStr(rec, "entity"), OptStr(rec, "state"));
                var look = AppearanceOf(rec);
                if (look != null) room.SetAppearance(cell, look);
            }

            foreach (var item in Arr(root, "objects"))
            {
                var rec = Obj(item, "对象记录");
                string category = Str(rec, "category");
                if (!Registries.Objects.Contains(category)) throw new FormatException("未知对象类别 " + category);
                room.AddObject(Str(rec, "id"), category, CellOf(room, rec, "对象"), AppearanceOf(rec));
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
                if (!rec.TryGetValue("params", out var raw) || !(raw is Dictionary<string, object> table)) throw new FormatException("缺少 params");
                var parameters = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (var p in table) parameters[p.Key] = Int(table, p.Key) / 1000f;
                recovery.Add(new RecoveryItem(id, Str(rec, "script"), duration / 1000f, parameters));
            }

            string primary = OptStr(root, "primaryEntranceId");
            if (primary != null && !doors.Any(d => d.Id == primary && d.Role == "entry"))
                throw new FormatException("主入口 " + primary + " 不是入口门");
            var extras = new Dictionary<string, string>(StringComparer.Ordinal);
            if (root.TryGetValue("extras", out var rawExtras) && rawExtras != null)
            {
                foreach (var pair in Obj(rawExtras, "extras"))
                    extras[pair.Key] = pair.Value as string ?? throw new FormatException("extras 字段 " + pair.Key + " 不是字符串");
            }
            var draft = new RoomDraftModel(room, doors, primary, extras, recovery);
            draft.Meta = new RoomMeta(OptStr(root, "name") ?? "", OptStr(root, "themeId"));
            ReadRest(root, draft);
            return draft;
        }

        static Dictionary<string, string> StringsIn(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out var v) || !(v is Dictionary<string, object> m)) throw new FormatException("缺少对象 " + key);
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in m) result[p.Key] = p.Value as string ?? throw new FormatException("字段 " + key + "." + p.Key + " 不是字符串");
            return result;
        }

        internal static BehaviorGraph GraphIn(object raw)
        {
            var g = Obj(raw, "行为图");
            var nodes = new List<BehaviorNode>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Arr(g, "nodes"))
            {
                var n = Obj(item, "行为节点");
                string id = Str(n, "id");
                if (!seen.Add(id)) throw new FormatException("行为图节点 ID 重复：" + id);
                Cell? cell = n.ContainsKey("x") ? new Cell(Int(n, "x"), Int(n, "y")) : (Cell?)null;
                nodes.Add(new BehaviorNode(id, Str(n, "kind"), cell, StringsIn(n, "params")));
            }
            var edges = new List<BehaviorEdge>();
            foreach (var item in Arr(g, "edges"))
            {
                var e = Obj(item, "连线");
                edges.Add(new BehaviorEdge(Str(e, "fromNode"), Str(e, "fromPort"), Str(e, "toNode"), Str(e, "toPort")));
            }
            return new BehaviorGraph(Str(g, "id"), OptStr(g, "owner"), nodes, edges);
        }

        internal static ObjectStateTable StatesIn(object raw)
        {
            if (raw == null) return null;
            var t = Obj(raw, "对象状态表");
            var states = new List<string>();
            foreach (var item in Arr(t, "states")) states.Add(item as string ?? throw new FormatException("状态名不是字符串"));
            var transitions = new List<StateTransition>();
            foreach (var item in Arr(t, "transitions"))
            {
                var rec = Obj(item, "状态转换");
                transitions.Add(new StateTransition(Str(rec, "from"), Str(rec, "trigger"), Str(rec, "to"), AppearanceOf(rec)));
            }
            return new ObjectStateTable(Str(t, "initial"), states, transitions);
        }

        static void ReadRest(Dictionary<string, object> root, RoomDraftModel draft)
        {
            var room = draft.Room;
            foreach (var item in Arr(root, "decorations"))
            {
                var rec = Obj(item, "装饰记录");
                string id = Str(rec, "id");
                room.Ids.Add(id, "decoration");
                draft.Decorations.Add(new Decoration(id, new Cell(Int(rec, "x"), Int(rec, "y")), AppearanceOf(rec)));
            }
            var assetIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Arr(root, "assets"))
            {
                var rec = Obj(item, "资源记录");
                string id = Str(rec, "id"), kind = Str(rec, "kind");
                if (!Registries.AssetKinds.Contains(kind)) throw new FormatException("未知资源类型 " + kind);
                if (!assetIds.Add(id)) throw new FormatException("资源 ID 重复：" + id);
                draft.Assets.Add(new AssetDescriptor(id, kind, Str(rec, "name"), Str(rec, "sha256"), Str(rec, "extension")));
            }
            foreach (var item in Arr(root, "graphs"))
            {
                var graph = GraphIn(item);
                room.Ids.Add(graph.Id, "graph");
                draft.Graphs.Add(graph);
            }
            foreach (var item in Arr(root, "templates"))
            {
                var rec = Obj(item, "模板记录");
                string id = Str(rec, "id");
                if (draft.Templates.ContainsKey(id)) throw new FormatException("模板 ID 重复：" + id);
                var graphs = Arr(rec, "graphs").Select(g => GraphIn(g)).ToList();
                rec.TryGetValue("states", out var rawStates);
                draft.Templates[id] = new ObjectTemplate(id, Str(rec, "name"), graphs, StatesIn(rawStates), AppearanceOf(rec));
            }
            foreach (var item in Arr(root, "instances"))
            {
                var rec = Obj(item, "模板实例记录");
                string id = Str(rec, "id");
                room.Ids.Add(id, "instance");
                draft.Instances.Add(new TemplateInstance(id, Str(rec, "templateId"), StringsIn(rec, "overrides")));
            }
            if (!root.TryGetValue("objectStates", out var rawTables) || !(rawTables is Dictionary<string, object> tables)) throw new FormatException("缺少对象 objectStates");
            foreach (var pair in tables) draft.ObjectStates[pair.Key] = StatesIn(pair.Value) ?? throw new FormatException("对象状态表为空：" + pair.Key);
            draft.UiImageAssetId = OptStr(root, "uiImage");
        }
    }
}
