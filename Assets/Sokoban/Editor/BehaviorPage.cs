/**
 * @file BehaviorPage.cs
 * @brief Open 读取所选格所有者的行为图，没有时创建尚未写入草稿的初始图。Draw 显示节点、连接和参数，Apply 经 RoomDraft.SetBehavior 提交 Core.BehaviorGraph。
 * 节点参数全部是字符串：condition 的 enabled 与 expression，setVisible 的 visible、targetRoom 与 targetObject，setState 的 state，lua 的 script；不再有 openPassage。
 */
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    internal sealed class BehaviorPage
    {
        readonly BehaviorCanvas canvas = new BehaviorCanvas();
        readonly Cell cell;
        readonly string roomId, graphId, ownerId;
        readonly int revision;
        readonly List<BehaviorNode> nodes;
        readonly List<BehaviorEdge> edges;
        ObjectStateEditor states;

        BehaviorPage(RoomDraft draft, Cell cell, BehaviorGraph graph)
        {
            this.cell = cell;
            roomId = draft.ActiveId;
            revision = draft.Revision;
            graphId = graph.Id;
            ownerId = graph.OwnerId;
            nodes = graph.Nodes.ToList();
            edges = graph.Edges.ToList();
        }

        internal static BehaviorPage Open(RoomDraft draft)
        {
            var cell = draft.SelectedCell.Value;
            var existing = draft.GraphOf(cell);
            return new BehaviorPage(draft, cell, existing ?? ObjectBehavior.Template(draft.OwnerAt(cell)));
        }

        static bool Flag(BehaviorNode node, string key, bool fallback) => ObjectBehavior.Parameter(node, key) is string v ? v != "false" : fallback;

        static string Text(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        internal bool Draw(LayoutPanel panel)
        {
            if (states != null) return states.Draw(panel);
            var draft = panel.Draft;
            EditorGUILayout.LabelField("对象行为", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(roomId + "  " + cell + "  " + (ownerId ?? "新绑定"));
            if (draft.CanEditStateProfile && GUILayout.Button("编辑对象状态与转换"))
            {
                states = new ObjectStateEditor(draft);
                return true;
            }
            canvas.Draw(nodes, edges);
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                switch (node.Kind)
                {
                    case BehaviorNodeKinds.RoomSolved: EditorGUILayout.LabelField("事件 · 房间解开"); break;
                    case BehaviorNodeKinds.Condition:
                        node = ObjectBehavior.WithParameter(node, "enabled", EditorGUILayout.Toggle("条件成立", Flag(node, "enabled", true)) ? "true" : "false");
                        node = ObjectBehavior.WithParameter(node, "expression", Text(EditorGUILayout.TextField("Lua 条件", ObjectBehavior.Parameter(node, "expression") ?? "")));
                        break;
                    case BehaviorNodeKinds.SetState:
                        var table = ownerId == null ? null : ObjectStateProfile.StatesOf(draft.Model, ownerId);
                        var names = table?.States.ToArray();
                        if (names == null || names.Length == 0) EditorGUILayout.HelpBox("先为当前物件添加对象状态。", MessageType.Info);
                        else node = ObjectBehavior.WithParameter(node, "state", names[EditorGUILayout.Popup("切换到状态", Mathf.Max(0, System.Array.IndexOf(names, ObjectBehavior.Parameter(node, "state"))), names)]);
                        break;
                    case BehaviorNodeKinds.Lua:
                        EditorGUILayout.LabelField("Lua 脚本");
                        node = ObjectBehavior.WithParameter(node, "script", EditorGUILayout.TextArea(ObjectBehavior.Parameter(node, "script") ?? "", GUILayout.MinHeight(52)));
                        break;
                    case BehaviorNodeKinds.SetVisible:
                        node = ObjectBehavior.WithParameter(node, "visible", EditorGUILayout.Toggle("动作 · 显示对象", Flag(node, "visible", false)) ? "true" : "false");
                        bool explicitTarget = EditorGUILayout.Toggle("指定其他对象", ObjectBehavior.Parameter(node, "targetObject") != null);
                        if (explicitTarget)
                        {
                            node = ObjectBehavior.WithParameter(node, "targetRoom", Text(EditorGUILayout.TextField("房间 ID（留空为本房）", ObjectBehavior.Parameter(node, "targetRoom") ?? "")));
                            node = ObjectBehavior.WithParameter(node, "targetObject", Text(EditorGUILayout.TextField("目标对象 ID", ObjectBehavior.Parameter(node, "targetObject") ?? "")) ?? "");
                        }
                        else node = ObjectBehavior.WithParameter(ObjectBehavior.WithParameter(node, "targetObject", null), "targetRoom", null);
                        break;
                    default: EditorGUILayout.LabelField(node.Kind ?? "未知节点"); break;
                }
                nodes[i] = node;
                foreach (var edge in edges.Where(e => e.FromNode == node.Id).ToList())
                {
                    var target = nodes.FirstOrDefault(n => n.Id == edge.ToNode);
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(edge.FromPort + " → " + (target == null ? "缺少目标" : BehaviorCanvas.Label(target.Kind)));
                    if (GUILayout.Button("断开", GUILayout.Width(48))) edges.Remove(edge);
                    GUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
            if (nodes.Count > 0 && GUILayout.Button("追加 Lua 动作"))
            {
                var last = nodes[nodes.Count - 1];
                var lua = ObjectBehavior.Node(BehaviorNodeKinds.Lua, new Dictionary<string, string> { ["script"] = "self.setVisible(false)" });
                edges.Add(new BehaviorEdge(last.Id, last.Kind == BehaviorNodeKinds.Condition ? BehaviorNodeKinds.TruePort : BehaviorNodeKinds.OutPort, lua.Id, BehaviorNodeKinds.InPort));
                nodes.Add(lua);
            }
            if (draft.ActiveId != roomId || draft.Revision != revision)
                EditorGUILayout.HelpBox("布局已变化，请返回后重新选择对象。", MessageType.Warning);
            else if (GUILayout.Button("应用行为并返回"))
            {
                panel.Run(() => draft.SetBehavior(cell, new BehaviorGraph(graphId, ownerId, nodes.ToList(), edges.ToList())), "已保存对象行为");
                return draft.Revision == revision && panel.Status != "已保存对象行为";
            }
            return !GUILayout.Button("返回布局并放弃本页修改");
        }
    }
}
