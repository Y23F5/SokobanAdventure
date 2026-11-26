/**
 * @file ObjectStateEditor.cs
 * @brief Draw 编辑所选对象的状态列表与转换，数据为 Core.ObjectStateTable；Apply 经 RoomDraft.SetStateTable 提交，不改动对象的玩法类别。
 * 转换的触发器取 firstPush、roomSolved、recoveryEnd，残留物是转换时在原格留下的纯表现外观，只在 firstPush 转换上可选；状态名同时作为外观 ID 解析（tree、logs 对应内置外观）。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    internal sealed class ObjectStateEditor
    {
        sealed class Edge { public string From, Trigger, To, Remnant; }

        readonly string owner;
        readonly string room;
        readonly string category;
        bool present;
        string initial = "initial";
        List<string> states = new List<string> { "initial" };
        List<Edge> edges = new List<Edge>();
        string stateName = "";

        internal ObjectStateEditor(RoomDraft draft)
        {
            owner = draft.SelectedCell.HasValue ? draft.OwnerAt(draft.SelectedCell.Value) : null;
            room = draft.ActiveId;
            category = draft.SelectedCategory;
            var table = owner == null ? null : ObjectStateProfile.StatesOf(draft.Model, owner);
            if (table != null) Load(table);
        }

        void Load(ObjectStateTable table)
        {
            present = true;
            initial = table.Initial;
            states = table.States.ToList();
            edges = table.Transitions.Select(t => new Edge { From = t.From, Trigger = t.Trigger, To = t.To, Remnant = t.Remnant?.Id }).ToList();
        }

        internal void UseForestPreset() => Load(ObjectStateProfile.ForestTable());

        ObjectStateTable Build() => present ? new ObjectStateTable(initial, states.ToList(),
            edges.Select(e => new StateTransition(e.From, e.Trigger, e.To, e.Remnant == null ? null : new AppearanceRef(e.Remnant))).ToList()) : null;

        internal void Apply(RoomDraft draft)
        {
            if (draft.ActiveId != room || draft.SelectedCell.HasValue == false || draft.OwnerAt(draft.SelectedCell.Value) != owner)
                throw new InvalidOperationException("对象选择已变化，请返回后重新选中");
            draft.SetStateTable(Build());
        }

        internal bool Draw(LayoutPanel panel)
        {
            var draft = panel.Draft;
            EditorGUILayout.LabelField("对象状态转换", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(owner);
            if (category == ObjectCategories.Box && GUILayout.Button("载入森林树木配置")) UseForestPreset();
            if (!present && GUILayout.Button("添加状态配置")) { present = true; initial = "initial"; states = new List<string> { "initial" }; edges.Clear(); }
            if (present) DrawTable(draft);
            if (GUILayout.Button("应用状态配置并返回"))
            {
                panel.Run(() => Apply(draft), "已应用对象状态配置");
                return panel.Status != "已应用对象状态配置";
            }
            return !GUILayout.Button("返回并放弃状态修改");
        }

        void DrawTable(RoomDraft draft)
        {
            var names = states.ToArray();
            initial = Pick("初始状态", initial, names, names);
            foreach (string state in states.ToArray())
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField(state, EditorStyles.boldLabel);
                if (states.Count > 1 && GUILayout.Button("删除状态"))
                {
                    edges.RemoveAll(e => e.From == state || e.To == state);
                    states.Remove(state);
                    if (initial == state) initial = states[0];
                }
                EditorGUILayout.EndVertical();
            }
            stateName = EditorGUILayout.TextField("新状态名称（即外观 ID）", stateName);
            if (!string.IsNullOrWhiteSpace(stateName) && !states.Contains(stateName.Trim()) && GUILayout.Button("添加状态"))
            { states.Add(stateName.Trim()); stateName = ""; }
            names = states.ToArray();
            bool box = category == ObjectCategories.Box;
            var triggerIds = box ? ObjectStateProfile.Triggers : new[] { TriggerIds.RoomSolved, TriggerIds.RecoveryEnd };
            var triggerLabels = box ? new[] { "首次成功推动", "房间解开", "恢复完成" } : new[] { "房间解开", "恢复完成" };
            var remnantIds = new string[] { null }.Concat(AppearanceLibrary.Entries(AppearanceLibrary.Remnant, null).Select(e => e.Id))
                .Concat(draft.Model.Assets.Where(a => a.Kind == AssetKinds.Model).Select(a => a.Id)).ToArray();
            var remnantNames = new[] { "无" }.Concat(AppearanceLibrary.Entries(AppearanceLibrary.Remnant, null).Select(e => e.Name))
                .Concat(draft.Model.Assets.Where(a => a.Kind == AssetKinds.Model).Select(a => a.Name)).ToArray();
            foreach (var edge in edges.ToArray())
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                edge.From = Pick("从状态", edge.From, names, names);
                edge.Trigger = Pick("触发条件", edge.Trigger, triggerIds, triggerLabels);
                edge.To = Pick("转换到", edge.To, names, names);
                if (edge.Trigger == TriggerIds.FirstPush) edge.Remnant = Pick("原格残留物（纯表现）", edge.Remnant, remnantIds, remnantNames);
                else edge.Remnant = null;
                if (GUILayout.Button("删除转换")) edges.Remove(edge);
                EditorGUILayout.EndVertical();
            }
            if (names.Length > 1 && GUILayout.Button("添加转换"))
                edges.Add(new Edge { From = names[0], To = names[1], Trigger = triggerIds[0] });
            if (GUILayout.Button("移除整个状态配置")) present = false;
        }

        static string Pick(string label, string current, string[] ids, string[] names) => ids[EditorGUILayout.Popup(label, Mathf.Max(0, Array.IndexOf(ids, current)), names)];
    }
}
