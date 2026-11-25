/**
 * @file FunctionDraft.cs
 * @brief DefineObject captures a decoration's visual and behavior as a room-local palette entry. SelectDefinition prepares the shared brush.
 * AddDefinedObject assigns each instance fresh behavior identities and binds self to its stable object ID in the same undoable placement.
 * DefinitionIssues validates template ownership, layout, and instance references before publishing.
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
        public string BrushDefinition { get; private set; }
        public bool CanDefineObject => Selected != null && Find(Selected) is JToken item && item.Parent == Room["decorations"] &&
            item["visual"]?["model"] != null && (item["stateProfile"] != null || Room["events"].Any(graph => (string)graph["ownerId"] == Selected));
        public JObject DefinitionVisual => Room["objectDefinitions"]?[BrushDefinition ?? ""]?["visual"] as JObject;
        public JToken DefinitionStateProfile => Room["objectDefinitions"]?[BrushDefinition ?? ""]?["stateProfile"];

        public void DefineObject(string name)
        {
            RoomFile.Check(CanDefineObject && !string.IsNullOrWhiteSpace(name), "请选择已有行为的装饰，并填写素材名称");
            var source = Find(Selected);
            var next = (JObject)Room.DeepClone();
            var definitions = (JObject)(next["objectDefinitions"] ??= new JObject());
            string id = "definition-" + Guid.NewGuid().ToString("N");
            var graphs = new JArray();
            foreach (var sourceGraph in Room["events"].Where(graph => (string)graph["ownerId"] == Selected))
            {
                var graphIssue = ObjectBehavior.GraphIssues(sourceGraph, false).FirstOrDefault();
                RoomFile.Check(graphIssue == null, graphIssue);
                var graph = ObjectBehavior.CopyFor((JObject)sourceGraph);
                foreach (var node in graph["nodes"])
                {
                    var target = node["target"];
                    if ((string)target?["objectId"] == Selected && ((string)target["levelId"] ?? ActiveId) == ActiveId)
                        ((JObject)node).Remove("target");
                }
                graphs.Add(graph);
            }
            definitions[id] = new JObject { ["name"] = name.Trim(), ["visual"] = source["visual"].DeepClone(), ["events"] = graphs };
            if (source["stateProfile"] != null) definitions[id]["stateProfile"] = source["stateProfile"].DeepClone();
            var issue = DefinitionIssues(next).FirstOrDefault();
            RoomFile.Check(issue == null, issue);
            Commit(next, Selected);
        }

        public void SelectDefinition(string id)
        {
            var definition = Room["objectDefinitions"]?[id];
            RoomFile.Check(definition is JObject, "当前房间没有所选功能物件");
            SelectAsset((string)definition["visual"]["model"]);
            BrushDefinition = id;
        }

        void AddDefinedObject(JObject room, JObject instance, string id)
        {
            if (BrushDefinition == null) return;
            var definition = room["objectDefinitions"][BrushDefinition];
            instance["definitionId"] = BrushDefinition;
            instance["name"] = definition["name"].DeepClone();
            instance["visual"] = definition["visual"].DeepClone();
            if (definition["stateProfile"] != null) instance["stateProfile"] = definition["stateProfile"].DeepClone();
            foreach (JObject template in definition["events"])
            {
                var graph = ObjectBehavior.CopyFor(template);
                graph["ownerId"] = id;
                ((JArray)room["events"]).Add(graph);
            }
        }

        internal static IEnumerable<string> DefinitionIssues(JObject room)
        {
            if (room["objectDefinitions"] != null && !(room["objectDefinitions"] is JObject))
            {
                yield return "功能物件定义必须是目录对象";
                yield break;
            }
            var definitions = room["objectDefinitions"] as JObject ?? new JObject();
            foreach (var entry in definitions.Properties())
            {
                var definition = entry.Value as JObject;
                if (string.IsNullOrWhiteSpace(entry.Name) || definition?["name"]?.Type != JTokenType.String ||
                    string.IsNullOrWhiteSpace((string)definition["name"]) || !(definition["events"] is JArray graphs) || graphs.Count == 0 && definition["stateProfile"] == null)
                {
                    yield return "功能物件定义缺少名称或行为：" + entry.Name;
                    continue;
                }
                var seen = new HashSet<string>();
                foreach (var graph in graphs)
                {
                    if (!(graph is JObject) || graph["ownerId"] != null)
                    {
                        yield return "功能物件模板必须由实例分配所有者：" + entry.Name;
                        continue;
                    }
                    string graphId = (string)graph["id"];
                    if (string.IsNullOrWhiteSpace(graphId) || !seen.Add(graphId)) yield return entry.Name + "：模板图 ID 缺失或重复";
                    foreach (string issue in ObjectBehavior.GraphIssues(graph, false))
                        yield return entry.Name + "：" + issue;
                    foreach (string issue in ObjectBehavior.StateNodeIssues(graph, definition["stateProfile"]))
                        yield return entry.Name + "：" + issue;
                }
            }
            foreach (var instance in RoomFile.Objects(room))
                if (instance["definitionId"] != null && (instance.Parent != room["decorations"] ||
                    instance["definitionId"].Type != JTokenType.String || definitions[(string)instance["definitionId"]] == null))
                    yield return "物件引用了无效的功能定义：" + instance["id"];
        }
    }
}
