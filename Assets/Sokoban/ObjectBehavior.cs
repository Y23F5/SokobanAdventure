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
        public void SetBehavior(Vector2Int cell, JObject graph)
        {
            RoomFile.Check(RoomFile.Terrain(Room, cell) != RoomFile.Void, "请选择地形或对象");
            var next = (JObject)Room.DeepClone();
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
            Commit(next, null);
            Select(cell);
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
        public static JObject CopyFor(JObject source)
        {
            var graph = (JObject)source.DeepClone();
            graph["id"] = Guid.NewGuid().ToString("N");
            graph.Remove("ownerId");
            var ids = new Dictionary<string,string>();
            foreach (var node in graph["nodes"])
            {
                string id = Guid.NewGuid().ToString("N");
                ids.Add((string)node["id"], id);
                node["id"] = id;
            }
            foreach (var edge in graph["edges"])
            {
                edge["from"] = ids[(string)edge["from"]];
                edge["to"] = ids[(string)edge["to"]];
            }
            return graph;
        }
        public static IEnumerable<string> Issues(JObject room)
        {
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
                var ids = new HashSet<string>();
                foreach (var node in nodes)
                {
                    string id = (string)node["id"];
                    if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) yield return $"行为图 {graphId} 节点 ID 无效 {id}";
                }
                foreach (var edge in edges)
                    if (!ids.Contains((string)edge["from"]) || !ids.Contains((string)edge["to"]) || (string)edge["in"] != "in" || string.IsNullOrEmpty((string)edge["out"]))
                        yield return "行为图存在悬空连线 " + graphId;
            }
        }
        public static bool IsVisual(JToken graph) => graph["nodes"] is JArray nodes && nodes.All(n =>
            (string)n["kind"] == "roomSolved" || (string)n["kind"] == "setVisible" || (string)n["kind"] == "condition" && n["expression"] == null);
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
                    case "condition": port=(bool?)node["enabled"] == true ? "true" : "false"; break;
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
