/**
 * @file ObjectStateProfile.cs
 * @brief Defines optional visual states, their trigger edges and their asset bindings. The stable entity remains in the original gameplay collection.
 * SetStateProfile validates and commits a whole profile in one edit. ForestProfile produces the forest configuration through the same format.
 */
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public static class ObjectStateProfile
    {
        public static readonly string[] Triggers = { "firstPush", "roomSolved", "restore" };
        public static readonly string[] SpecialModels = { "ForestTree", "TreeStump" };
        public static string Prefab(string id) => id == "ForestTree" ? "Diorama/Tree" : id == "TreeStump" ? "LayoutModels/TreeStump" : AppearanceLibrary.Find(id)?.Prefab;
        public static JToken Transition(JToken profile, string state, string trigger) => profile?["transitions"]?.FirstOrDefault(edge =>
            (string)edge["from"] == state && (string)edge["trigger"] == trigger);

        public static JObject ForestProfile() => new JObject
        {
            ["initial"] = "tree",
            ["states"] = new JObject
            {
                ["tree"] = new JObject { ["builtinModel"] = "ForestTree" },
                ["logs"] = new JObject { ["builtinModel"] = "Logs" }
            },
            ["transitions"] = new JArray
            {
                new JObject { ["from"] = "tree", ["to"] = "logs", ["trigger"] = "firstPush", ["leave"] = "TreeStump" },
                new JObject { ["from"] = "logs", ["to"] = "tree", ["trigger"] = "restore", ["depart"] = "forestDecay", ["arrive"] = "grow" }
            }
        };

        internal static void Validate(JObject room, JToken token, RoomTool type)
        {
            if (token == null) return;
            var states = token is JObject profile ? profile["states"] as JObject : null;
            RoomFile.Check((type == RoomTool.Box || type == RoomTool.Decoration) && states != null && states.Count > 0 &&
                token["initial"]?.Type == JTokenType.String && states[(string)token["initial"]] != null && token["transitions"] is JArray,
                "物件状态需要初始状态、状态列表和转换列表");
            foreach (var state in states.Properties())
            {
                var appearance = state.Value as JObject;
                RoomFile.Check(!string.IsNullOrWhiteSpace(state.Name) && appearance != null, "状态的名称或外观无效");
                string builtin = (string)appearance["builtinModel"];
                RoomFile.Check(builtin == null || Prefab(builtin) != null && appearance["visual"]?["model"] == null, "状态模型无效：" + state.Name);
                RoomAssets.ValidateVisual(room["assets"] as JObject, appearance["visual"], state.Name, false);
            }
            var keys = new System.Collections.Generic.HashSet<(string, string)>();
            foreach (var edge in (JArray)token["transitions"])
            {
                var link = edge as JObject;
                string from = (string)link?["from"], to = (string)link?["to"], trigger = (string)link?["trigger"];
                RoomFile.Check(from != null && to != null && states[from] != null && states[to] != null && Triggers.Contains(trigger) &&
                    keys.Add((from,trigger)) && (trigger != "firstPush" || type == RoomTool.Box), "状态转换不完整、重复或触发条件不适用");
                string leave = (string)link["leave"], depart = (string)link["depart"], arrive = (string)link["arrive"];
                RoomFile.Check(leave == null || trigger == "firstPush" && (Prefab(leave) != null ||
                    (string)room["assets"]?[leave]?["kind"] == "model"), "原地遗留物需要可推动的对象和有效模型");
                RoomFile.Check((depart == null || trigger == "restore" && depart == "forestDecay") &&
                    (arrive == null || trigger == "restore" && arrive == "grow"), "恢复动画配置无效");
                foreach (string part in new[] { "depart", "arrive" })
                    if ((string)link[part + "Animation"] is string animation)
                        RoomFile.Check(trigger == "restore" && link[part] == null && (string)room["assets"]?[animation]?["kind"] == "animation",
                            "恢复转换引用了无效的导入动作：" + animation);
            }
        }
    }

    public sealed partial class RoomDraft
    {
        public bool CanEditStateProfile => Selected != null && (SelectedAppearanceType == RoomTool.Box || SelectedAppearanceType == RoomTool.Decoration);

        public void SetStateProfile(JObject profile)
        {
            RoomFile.Check(CanEditStateProfile, "请选择一个箱子或装饰物件");
            var next = (JObject)Room.DeepClone();
            var cell = SelectedCell.Value;
            var item = (JObject)PropertyObject(next, cell);
            if (profile == null) item.Remove("stateProfile");
            else
            {
                foreach (var state in ((JObject)profile["states"]).Properties())
                    foreach (var slot in (state.Value["visual"] as JObject)?.Properties() ?? Enumerable.Empty<JProperty>())
                        CopyAssetInto(next, (string)slot.Value);
                foreach (var edge in profile["transitions"])
                    foreach (string field in new[] { "departAnimation", "arriveAnimation", "leave" })
                        if ((string)edge[field] is string id && ObjectStateProfile.Prefab(id) == null) CopyAssetInto(next, id);
                ObjectStateProfile.Validate(next, profile, SelectedAppearanceType);
                item["stateProfile"] = profile.DeepClone();
            }
            if (JToken.DeepEquals(next, Room)) return;
            Commit(next, Selected);
            Select(cell);
        }
    }
}
