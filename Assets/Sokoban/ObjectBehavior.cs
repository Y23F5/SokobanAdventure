/**
 * @file ObjectBehavior.cs
 * @brief OwnerAt 在已占用格或绑定地形上解析稳定身份。SetBehavior 将图与首次生成的地形身份一起记入历史。
 * Template 创建房间解开、条件、隐藏对象三个节点和两条边。CopyFor 给复制图及节点分配独立 ID 并重连边。
 * Effects 按具名端口遍历当前对象图，IsVisual 判断求解时可忽略的纯表现节点。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public sealed partial class RoomDraft
    {
        public string OwnerAt(Vector2Int cell)
        {
            var entity = RoomFile.Objects(Room).FirstOrDefault(item => RoomFile.At(item) == cell)
                ?? Room["terrainObjects"]?.FirstOrDefault(item => RoomFile.At(item) == cell);
            return (string)entity?["id"];
        }
        public bool TryGetSelectedName(out string name)
        {
            var names = Selection.Select(point => (string)PropertyObject(Room, point)?["name"] ?? "").Distinct().Take(2).ToArray();
            name = names.Length == 1 ? names[0] : "";
            return names.Length == 1;
        }
        static JToken PropertyObject(JObject room, Vector2Int point) => RoomFile.Objects(room).FirstOrDefault(item => RoomFile.At(item) == point)
            ?? room["terrainObjects"]?.FirstOrDefault(item => RoomFile.At(item) == point);
        public void SetSelectedName(string name)
        {
            RoomFile.Check(Selection.Count > 0 && SelectedCell.HasValue, "请选择对象或地形");
            var next = (JObject)Room.DeepClone();
            var selected = Selection.ToArray();
            var primary = SelectedCell.Value;
            foreach (var point in selected)
            {
                var obj = PropertyObject(next, point);
                if (obj == null)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (next["terrainObjects"] == null) next["terrainObjects"] = new JArray();
                    obj = new JObject { ["id"]="tile-" + Guid.NewGuid().ToString("N"), ["cell"]=RoomFile.Point(point) };
                    ((JArray)next["terrainObjects"]).Add(obj);
                }
                if (string.IsNullOrWhiteSpace(name)) ((JObject)obj).Remove("name");
                else obj["name"] = name.Trim();
            }
            if (JToken.DeepEquals(next,Room)) return;
            Commit(next,null);
            Select(primary);
            foreach (var point in selected) Selection.Add(point);
        }
        public void SetBehavior(Vector2Int cell, JObject graph)
        {
            var cells = Selection.Contains(cell) ? Selection.ToArray() : new[] { cell };
            var next = (JObject)Room.DeepClone();
            var original = Room["events"].FirstOrDefault(g => (string)g["id"] == (string)graph["id"]);
            foreach (var target in cells)
            {
                RoomFile.Check(RoomFile.Terrain(Room, target) != RoomFile.Void, "请选择地形或对象");
                var value = graph;
                if (target != cell)
                {
                    string owner = OwnerAt(target);
                    string family = (string)graph["copySource"] ?? (string)graph["id"];
                    var siblings = Room["events"].Where(g => (string)g["ownerId"] == owner).ToList();
                    var previous = original == null ? null : siblings.FirstOrDefault(g => ((string)g["copySource"] ?? (string)g["id"]) == family);
                    if (previous == null && original != null && siblings.Count == 1 && siblings[0]["copySource"] == null && graph["copySource"] == null)
                        previous = siblings[0];
                    var source = (JObject)graph.DeepClone();
                    source["ownerId"] = OwnerAt(cell);
                    source["copySource"] = family;
                    value = ObjectBehavior.CopyFor(source, original, previous);
                }
                WriteBehavior(next, target, value);
            }
            if (JToken.DeepEquals(next, Room)) return;
            Commit(next, null);
            Select(cell);
            foreach (var target in cells) Selection.Add(target);
        }
        void WriteBehavior(JObject next, Vector2Int cell, JObject graph)
        {
            string owner = OwnerAt(cell);
            if (owner == null)
            {
                owner = "tile-" + Guid.NewGuid().ToString("N");
                if (next["terrainObjects"] == null) next["terrainObjects"] = new JArray();
                ((JArray)next["terrainObjects"]).Add(new JObject { ["id"] = owner, ["cell"] = RoomFile.Point(cell) });
            }
            var value = (JObject)graph.DeepClone();
            value["ownerId"] = owner;
            var events = (JArray)next["events"];
            var previous = events.FirstOrDefault(item => (string)item["id"] == (string)value["id"]);
            if (previous == null) events.Add(value);
            else
            {
                RoomFile.Check((string)previous["ownerId"] == owner, "该行为属于其他对象");
                if (JToken.DeepEquals(previous, value)) return;
                previous.Replace(value);
            }
        }
    }
    public static class ObjectBehavior
    {
        public static JObject Template()
        {
            var graph = new JObject { ["nodes"] = new JArray(
                new JObject { ["id"]="start", ["kind"]="roomSolved" },
                new JObject { ["id"]="when", ["kind"]="condition", ["enabled"]=true },
                new JObject { ["id"]="hide", ["kind"]="setVisible", ["visible"]=false }),
                ["edges"] = new JArray(Edge("start", "out", "when"), Edge("when", "true", "hide")) };
            return CopyFor(graph);
        }
        static JObject Edge(string from, string port, string to) => new JObject { ["from"]=from, ["out"]=port, ["to"]=to, ["in"]="in" };
        public static JObject CopyFor(JObject source, JToken original = null, JToken previous = null)
        {
            var graph = (JObject)source.DeepClone();
            graph["id"] = (string)previous?["id"] ?? Guid.NewGuid().ToString("N");
            graph.Remove("ownerId");
            if (source["ownerId"] != null || original != null) graph["copySource"] = (string)source["copySource"] ?? (string)source["id"];
            var ids = new Dictionary<string,string>();
            var counterparts = new Dictionary<string,string>();
            if (previous?["nodes"] is JArray existing)
            {
                foreach (var candidate in existing)
                    counterparts[(string)candidate["copySource"] ?? (string)candidate["id"]] = (string)candidate["id"];
                if (previous["copySource"] == null && original?["nodes"] is JArray before && before.Count == existing.Count &&
                    before.Select(n => (string)n["kind"]).SequenceEqual(existing.Select(n => (string)n["kind"])))
                    for (int i = 0; i < before.Count; i++)
                        if (existing[i]["copySource"] == null) counterparts[(string)before[i]["id"]] = (string)existing[i]["id"];
            }
            foreach (var node in graph["nodes"])
            {
                string sourceId = (string)node["id"];
                string lineage = (string)node["copySource"] ?? sourceId;
                if (!counterparts.TryGetValue(lineage, out var id)) id = Guid.NewGuid().ToString("N");
                ids.Add(sourceId, id);
                if (graph["copySource"] != null) node["copySource"] = lineage;
                node["id"] = id;
            }
            foreach (var edge in graph["edges"])
            {
                edge["from"] = ids[(string)edge["from"]];
                edge["to"] = ids[(string)edge["to"]];
            }
            return graph;
        }
        public static IEnumerable<string> TargetIssues(JObject room, JObject world = null, JObject catalog = null)
        {
            string here = (string)room["levelId"];
            foreach (var graph in room["events"])
                foreach (var node in graph["nodes"] as JArray ?? new JArray())
                {
                    if (node["target"] == null) continue;
                    var target = node["target"] as JObject;
                    string id = (string)target?["objectId"], level = (string)target?["levelId"] ?? here;
                    string location = $"{here}/{graph["ownerId"]}/{graph["id"]}/{node["id"]}";
                    if ((string)node["kind"] != "setVisible" || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(level))
                    { yield return location + " 的显式目标无效"; continue; }
                    if (level != here && world == null) continue;
                    var targetRoom = level == here ? room : catalog?[level] as JObject;
                    if (targetRoom == null || level != here && !world["rooms"].Any(r => (string)r["levelId"] == level) ||
                        !RoomFile.Objects(targetRoom).Concat(targetRoom["terrainObjects"] as JArray ?? new JArray()).Any(o => (string)o["id"] == id))
                        yield return $"{location} 找不到目标 {level}/{id}";
                }
        }
        public static IEnumerable<string> Issues(JObject room)
        {
            foreach (var issue in TargetIssues(room)) yield return issue;
            var owners = new HashSet<string>(RoomFile.Objects(room).Select(item => (string)item["id"]));
            var occupied = new HashSet<Vector2Int>();
            foreach (var tile in room["terrainObjects"] as JArray ?? new JArray())
            {
                string owner = (string)tile["id"];
                var cell = RoomFile.At(tile);
                if (string.IsNullOrWhiteSpace(owner) || !owners.Add(owner) || !occupied.Add(cell) || RoomFile.Terrain(room, cell) == RoomFile.Void)
                    yield return $"地形身份无效 {owner} {cell}";
            }
            var graphIds = new HashSet<string>();
            foreach (var graph in room["events"])
            {
                string graphId = (string)graph["id"];
                if (string.IsNullOrWhiteSpace(graphId) || !graphIds.Add(graphId)) yield return "行为图 ID 无效 " + graphId;
                if (!owners.Contains((string)graph["ownerId"])) yield return $"行为图 {graphId} 找不到对象 {graph["ownerId"]}";
                if (!(graph["nodes"] is JArray nodes) || !(graph["edges"] is JArray edges)) { yield return "行为图缺少节点或连线 " + graphId; continue; }
                if (nodes.Any(n => (string)n["kind"] == "openPassage"))
                {
                    var tile = room["terrainObjects"]?.FirstOrDefault(t => (string)t["id"] == (string)graph["ownerId"]);
                    if (tile == null || RoomFile.Terrain(room, RoomFile.At(tile)) != '#') yield return "开放墙格需要绑定墙体 " + graphId;
                }
                var kinds = new Dictionary<string, string>();
                foreach (var node in nodes)
                {
                    string id = (string)node["id"];
                    if (string.IsNullOrWhiteSpace(id) || kinds.ContainsKey(id))
                        yield return $"行为图 {graphId} 节点 ID 无效 {id}";
                    else kinds.Add(id, (string)node["kind"]);
                }
                foreach (var edge in edges)
                {
                    string from = (string)edge["from"] ?? "", to = (string)edge["to"] ?? "";
                    string output = (string)edge["out"], input = (string)edge["in"];
                    if (!kinds.TryGetValue(from, out var source) || !kinds.TryGetValue(to, out var destination))
                    {
                        yield return $"行为图 {graphId} 存在悬空连线 {from} → {to}";
                        continue;
                    }
                    if (destination == "roomSolved" || input != "in")
                        yield return $"行为图 {graphId} 节点 {to} 没有输入端口 {input}";
                    bool allowed = source switch
                    {
                        "condition" => output == "true" || output == "false",
                        "roomSolved" or "openPassage" or "setVisible" or "lua" => output == "out",
                        _ => !string.IsNullOrEmpty(output)
                    };
                    if (!allowed) yield return $"行为图 {graphId} 节点 {from} 没有输出端口 {output}";
                }
            }
        }
        public static bool IsVisualNode(JToken node)
        {
            string kind = (string)node["kind"];
            return kind == "roomSolved" || kind == "setVisible" || kind == "condition" && node["expression"] == null;
        }
        public static bool CanSimulateNode(JToken node) => IsVisualNode(node) || (string)node["kind"] == "openPassage";
        public static bool CanSimulate(JToken graph) => graph["nodes"] is JArray nodes && nodes.All(CanSimulateNode);
        public static HashSet<Vector2Int> SolvedPassages(JObject room)
        {
            var result = new HashSet<Vector2Int>();
            foreach (var graph in room["events"])
            {
                if (!CanSimulate(graph) || !graph["nodes"].Any(n => (string)n["kind"] == "openPassage")) continue;
                var owner = room["terrainObjects"]?.FirstOrDefault(t => (string)t["id"] == (string)graph["ownerId"]);
                if (owner != null && Effects(graph).Any(n => (string)n["kind"] == "openPassage")) result.Add(RoomFile.At(owner));
            }
            return result;
        }
        public static bool Walkable(JObject room, Vector2Int cell, bool unlocked, HashSet<Vector2Int> passages) =>
            RoomFile.Floor(room, cell) || unlocked && passages.Contains(cell);
        public static bool IsVisual(JToken graph) => graph["nodes"] is JArray nodes && nodes.All(IsVisualNode);
        public static IEnumerable<JToken> Effects(JToken graph)
        {
            var nodes = graph["nodes"] as JArray;
            if (nodes == null || !(graph["edges"] is JArray edges)) yield break;
            var queue = new Queue<JToken>(nodes.Where(n => (string)n["kind"] == "roomSolved"));
            var seen = new HashSet<string>();
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                string id = (string)node["id"];
                if (!seen.Add(id)) continue;
                string port;
                switch ((string)node["kind"])
                {
                    case "roomSolved": port="out"; break;
                    case "condition":
                        bool matched = (bool?)node["enabled"] == true && BehaviorLua.Evaluate((string)node["expression"], (string)graph["ownerId"], true, null);
                        port = matched ? "true" : "false";
                        break;
                    case "lua":
                        var effects = new List<JToken>();
                        BehaviorLua.Evaluate((string)node["script"], (string)graph["ownerId"], false, effects);
                        foreach (var effect in effects) yield return effect;
                        port="out";
                        break;
                    case "openPassage":
                    case "setVisible": yield return node; port="out"; break;
                    default: continue;
                }
                foreach (var edge in edges.Where(e => (string)e["from"] == id && (string)e["out"] == port && (string)e["in"] == "in"))
                {
                    var target = nodes.FirstOrDefault(n => (string)n["id"] == (string)edge["to"]);
                    if (target != null) queue.Enqueue(target);
                }
            }
        }
    }
}
