/**
 * @file BehaviorCanvas.cs
 * @brief Draw 绘制可拖动节点及其连接，节点与连线是 Core.BehaviorNode 与 BehaviorEdge 的可变列表，画布坐标存在节点参数 canvasX、canvasY。
 * Port 从输出端选择连线起点，再点目标输入端建立连接，输入输出端口取自行为节点注册表。Add 创建带默认参数的节点，节点类型取自 Registries.BehaviorNodes，不再有 openPassage。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sokoban.Core;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    internal sealed class BehaviorCanvas
    {
        Vector2 scroll;
        string drag, source, outlet;
        Vector2 offset;
        const float Width = 132, Height = 66;
        static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            [BehaviorNodeKinds.RoomSolved] = "房间解开", [BehaviorNodeKinds.Condition] = "条件", [BehaviorNodeKinds.SetVisible] = "显示对象",
            [BehaviorNodeKinds.Lua] = "Lua", [BehaviorNodeKinds.SetState] = "切换对象状态"
        };
        static readonly string[] Kinds = Registries.BehaviorNodes.Ids.ToArray();
        int selectedKind;

        internal static string Label(string kind) => Labels.TryGetValue(kind ?? "", out var label) ? label : kind;

        static float Coordinate(BehaviorNode node, string key, float fallback) =>
            float.TryParse(ObjectBehavior.Parameter(node, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

        static Rect Position(BehaviorNode node, int index) =>
            new Rect(Coordinate(node, "canvasX", 12 + 148 * (index % 4)), Coordinate(node, "canvasY", 14 + 88 * (index / 4)), Width, Height);

        internal void Draw(List<BehaviorNode> nodes, List<BehaviorEdge> edges)
        {
            GUILayout.BeginHorizontal();
            selectedKind = EditorGUILayout.Popup(selectedKind, Kinds.Select(Label).ToArray());
            if (GUILayout.Button("添加节点")) Add(nodes);
            GUILayout.EndHorizontal();
            var viewport = GUILayoutUtility.GetRect(80, 218, GUILayout.ExpandWidth(true));
            var bounds = new Rect(0, 0, 640, 310);
            for (int i = 0; i < nodes.Count; i++)
            {
                Rect rect = Position(nodes[i], i);
                bounds.width = Mathf.Max(bounds.width, rect.xMax + 50);
                bounds.height = Mathf.Max(bounds.height, rect.yMax + 40);
            }
            scroll = GUI.BeginScrollView(viewport, scroll, bounds);
            if (Event.current.type == EventType.Repaint)
                foreach (var edge in edges)
                {
                    int fromIndex = nodes.FindIndex(n => n.Id == edge.FromNode), toIndex = nodes.FindIndex(n => n.Id == edge.ToNode);
                    if (fromIndex < 0 || toIndex < 0) continue;
                    var a = Position(nodes[fromIndex], fromIndex); var b = Position(nodes[toIndex], toIndex);
                    var start = new Vector3(a.xMax, a.center.y); var end = new Vector3(b.xMin, b.center.y);
                    Handles.DrawBezier(start, end, start + Vector3.right * 36, end + Vector3.left * 36, Color.cyan, null, 2);
                }
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i]; string id = node.Id;
                Registries.BehaviorNodes.TryGet(node.Kind, out var kind);
                var rect = Position(node, i);
                GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
                var title = new Rect(rect.x + 4, rect.y + 3, Width - 30, 22);
                GUI.Label(title, Label(node.Kind), EditorStyles.boldLabel);
                var ev = Event.current;
                if (ev.type == EventType.MouseDown && ev.button == 0 && title.Contains(ev.mousePosition)) { drag = id; offset = ev.mousePosition - rect.position; ev.Use(); }
                if (ev.type == EventType.MouseDrag && drag == id) { var p = ev.mousePosition - offset; nodes[i] = ObjectBehavior.WithCanvas(node, Mathf.Max(0, p.x), Mathf.Max(0, p.y)); ev.Use(); }
                if (ev.rawType == EventType.MouseUp && drag == id) drag = null;
                if (GUI.Button(new Rect(rect.xMax - 25, rect.y + 3, 22, 20), "×"))
                {
                    edges.RemoveAll(e => e.FromNode == id || e.ToNode == id);
                    nodes.RemoveAt(i); if (source == id) source = null; break;
                }
                bool hasInput = kind != null && kind.Inputs.Contains(BehaviorNodeKinds.InPort);
                if (hasInput && GUI.Button(new Rect(rect.x + 3, rect.y + 31, 34, 22), "入") && source != null && source != id)
                {
                    if (!edges.Any(e => e.FromNode == source && e.FromPort == outlet && e.ToNode == id))
                        edges.Add(new BehaviorEdge(source, outlet, id, BehaviorNodeKinds.InPort));
                    source = null;
                }
                if (kind == null) continue;
                if (node.Kind == BehaviorNodeKinds.Condition) { Port(rect, id, BehaviorNodeKinds.TruePort, "真", 44); Port(rect, id, BehaviorNodeKinds.FalsePort, "假", 88); }
                else if (kind.Outputs.Count > 0) Port(rect, id, kind.Outputs[0], "出", 88);
            }
            GUI.EndScrollView();
            if (source != null && GUILayout.Button("取消连线")) source = null;
        }

        void Port(Rect rect, string id, string port, string title, float x)
        {
            if (GUI.Button(new Rect(rect.x + x, rect.y + 31, 38, 22), title)) { source = id; outlet = port; }
        }

        void Add(List<BehaviorNode> nodes)
        {
            string kind = Kinds[selectedKind];
            var parameters = new Dictionary<string, string>();
            if (kind == BehaviorNodeKinds.Condition) parameters["enabled"] = "true";
            if (kind == BehaviorNodeKinds.SetVisible) parameters["visible"] = "false";
            if (kind == BehaviorNodeKinds.Lua) parameters["script"] = "self.setVisible(false)";
            nodes.Add(ObjectBehavior.Node(kind, parameters));
        }
    }
}
