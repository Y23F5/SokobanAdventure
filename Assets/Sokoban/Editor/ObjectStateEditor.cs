/**
 * @file ObjectStateEditor.cs
 * @brief Draw edits state appearances and transition edges for the selected stable object. Apply uses the shared draft command and leaves gameplay type untouched.
 */
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    internal sealed class ObjectStateEditor
    {
        readonly string owner;
        readonly string room;
        JObject value;
        string stateName = "";

        internal ObjectStateEditor(RoomDraft draft)
        {
            owner = draft.Selected;
            room = draft.ActiveId;
            value = draft.Find(owner)?["stateProfile"]?.DeepClone() as JObject;
        }

        internal void UseForestPreset() => value = ObjectStateProfile.ForestProfile();
        internal void Apply(RoomDraft draft)
        {
            RoomFile.Check(draft.ActiveId == room && draft.Selected == owner, "对象选择已变化，请返回后重新选中");
            draft.SetStateProfile(value);
        }

        internal bool Draw(LayoutPanel panel)
        {
            var draft = panel.Draft;
            EditorGUILayout.LabelField("物件状态转换", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(owner);
            if (draft.SelectedAppearanceType == RoomTool.Box && GUILayout.Button("载入森林树木配置")) UseForestPreset();
            if (value == null && GUILayout.Button("添加状态配置"))
                value = new JObject { ["initial"] = "initial", ["states"] = new JObject { ["initial"] = new JObject() }, ["transitions"] = new JArray() };
            if (value != null) DrawProfile(draft);
            if (GUILayout.Button("应用状态配置并返回"))
            {
                panel.Run(() => Apply(draft), "已应用物件状态配置");
                return panel.Status != "已应用物件状态配置";
            }
            return !GUILayout.Button("返回并放弃状态修改");
        }

        void DrawProfile(RoomDraft draft)
        {
            var states = (JObject)value["states"];
            var transitions = (JArray)value["transitions"];
            var names = states.Properties().Select(item => item.Name).ToArray();
            value["initial"] = Pick("初始状态", (string)value["initial"], names, names);
            var builtins = ObjectStateProfile.SpecialModels.Concat(new[] { RoomTool.Box, RoomTool.Decoration, RoomTool.Target, RoomTool.Wall, RoomTool.Floor }
                .SelectMany(type => AppearanceLibrary.Entries(type, null).Select(entry => entry.Id))).ToArray();
            var builtinNames = builtins.Select(id => id == "ForestTree" ? "完整树木" : id == "TreeStump" ? "树桩" : AppearanceLibrary.Find(id).Name).ToArray();
            var assets = draft.Catalog.Properties().SelectMany(item => (item.Value["assets"] as JObject)?.Properties()
                ?? Enumerable.Empty<JProperty>()).Select(item => item.Name).Distinct().ToArray();
            var models = assets.Where(id => (string)draft.AssetDefinition(id)["kind"] == "model").ToArray();
            var animations = assets.Where(id => (string)draft.AssetDefinition(id)["kind"] == "animation").ToArray();

            foreach (var entry in states.Properties().ToArray())
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(entry.Name, EditorStyles.boldLabel);
                var state = (JObject)entry.Value;
                var visual = state["visual"] as JObject ?? new JObject();
                string model = Pick("模型", (string)state["builtinModel"] ?? (string)visual["model"],
                    new string[] { null }.Concat(builtins).Concat(models).ToArray(),
                    new[] { "物件自身外观" }.Concat(builtinNames).Concat(models.Select(id => (string)draft.AssetDefinition(id)["name"])).ToArray());
                Set(state, "builtinModel", builtins.Contains(model) ? model : null);
                Set(visual, "model", models.Contains(model) ? model : null);
                Set(visual, "animation", Pick("状态动画", (string)visual["animation"], new string[] { null }.Concat(animations).ToArray(),
                    new[] { "模型自带／无" }.Concat(animations.Select(id => (string)draft.AssetDefinition(id)["name"])).ToArray()));
                if (visual.HasValues) state["visual"] = visual;
                else state.Remove("visual");
                if (names.Length > 1 && GUILayout.Button("删除状态"))
                {
                    foreach (var edge in transitions.Where(edge => (string)edge["from"] == entry.Name || (string)edge["to"] == entry.Name).ToArray()) edge.Remove();
                    entry.Remove();
                    if ((string)value["initial"] == entry.Name) value["initial"] = states.Properties().First().Name;
                }
                EditorGUILayout.EndVertical();
            }
            stateName = EditorGUILayout.TextField("新状态名称", stateName);
            if (!string.IsNullOrWhiteSpace(stateName) && states[stateName.Trim()] == null && GUILayout.Button("添加状态"))
            { states[stateName.Trim()] = new JObject(); stateName = ""; }
            names = states.Properties().Select(item => item.Name).ToArray();
            bool box = draft.SelectedAppearanceType == RoomTool.Box;
            var triggerIds = box ? ObjectStateProfile.Triggers : new[] { "roomSolved", "restore" };
            var triggerLabels = box ? new[] { "首次成功推动", "房间解开", "使用恢复道具" } : new[] { "房间解开", "使用恢复道具" };
            foreach (var edge in transitions.OfType<JObject>().ToArray())
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                edge["from"] = Pick("从状态", (string)edge["from"], names, names);
                edge["trigger"] = Pick("触发条件", (string)edge["trigger"], triggerIds, triggerLabels);
                edge["to"] = Pick("转换到", (string)edge["to"], names, names);
                string trigger = (string)edge["trigger"];
                if (trigger == "firstPush") Set(edge, "leave", Pick("原格遗留物（可通行）", (string)edge["leave"],
                    new string[] { null }.Concat(builtins).Concat(models).ToArray(),
                    new[] { "无" }.Concat(builtinNames).Concat(models.Select(id => (string)draft.AssetDefinition(id)["name"])).ToArray()));
                else edge.Remove("leave");
                if (trigger == "restore")
                {
                    PickRecoveryMotion(draft, edge, "depart", "当前位置演出", "forestDecay", "木束腐朽", animations);
                    PickRecoveryMotion(draft, edge, "arrive", "恢复位置演出", "grow", "状态模型生长", animations);
                }
                else { edge.Remove("depart"); edge.Remove("arrive"); edge.Remove("departAnimation"); edge.Remove("arriveAnimation"); }
                if (GUILayout.Button("删除转换")) edge.Remove();
                EditorGUILayout.EndVertical();
            }
            if (names.Length > 1 && GUILayout.Button("添加转换"))
                transitions.Add(new JObject { ["from"] = names[0], ["to"] = names[1], ["trigger"] = triggerIds[0] });
            if (GUILayout.Button("移除整个状态配置")) value = null;
        }

        static void PickRecoveryMotion(RoomDraft draft, JObject edge, string field, string label, string preset, string presetName, string[] imported)
        {
            string motion = Pick(label, (string)edge[field] ?? (string)edge[field + "Animation"], new[] { null, preset }.Concat(imported).ToArray(),
                new[] { "无", presetName }.Concat(imported.Select(id => (string)draft.AssetDefinition(id)["name"])).ToArray());
            Set(edge, field, motion == preset ? preset : null);
            Set(edge, field + "Animation", motion != preset ? motion : null);
        }

        static string Pick(string label, string current, string[] ids, string[] names) => ids[EditorGUILayout.Popup(label, Mathf.Max(0, Array.IndexOf(ids, current)), names)];
        static void Set(JObject obj, string key, string value) { if (value == null) obj.Remove(key); else obj[key] = value; }
    }
}
