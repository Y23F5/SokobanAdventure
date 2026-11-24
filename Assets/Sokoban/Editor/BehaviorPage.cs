/**
 * @file BehaviorPage.cs
 * @brief Open 读取所选格上的对象图或创建尚未写入草稿的初始图。Draw 显示节点、连接和条件参数，Apply 通过共享命令提交编辑。
 */
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    internal sealed class BehaviorPage
    {
        readonly BehaviorCanvas canvas = new BehaviorCanvas();
        readonly Vector2Int cell;
        readonly string roomId;
        readonly int revision;
        readonly JObject graph;
        BehaviorPage(RoomDraft draft, Vector2Int cell, JObject graph)
        {
            this.cell = cell;
            this.graph = graph;
            roomId = draft.ActiveId;
            revision = draft.Revision;
        }
        internal static BehaviorPage Open(RoomDraft draft)
        {
            var cell = draft.SelectedCell.Value;
            string owner = draft.OwnerAt(cell);
            var existing = draft.Room["events"].FirstOrDefault(g => owner != null && (string)g["ownerId"] == owner);
            return new BehaviorPage(draft, cell, existing == null ? ObjectBehavior.Template() : (JObject)existing.DeepClone());
        }
        internal bool Draw(LayoutPanel panel)
        {
            EditorGUILayout.LabelField("对象行为", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(roomId + "  " + cell + "  " + ((string)graph["ownerId"] ?? "新绑定"));
            canvas.Draw(graph);
            var nodes = graph["nodes"] as JArray;
            if (nodes != null) foreach (var node in nodes)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                string kind = (string)node["kind"];
                switch (kind)
                {
                    case "roomSolved": EditorGUILayout.LabelField("事件 · 房间解开"); break;
                    case "condition":
                        node["enabled"] = EditorGUILayout.Toggle("条件成立", (bool?)node["enabled"] ?? true);
                        string expression = EditorGUILayout.TextField("Lua 条件", (string)node["expression"] ?? "");
                        if (!string.IsNullOrWhiteSpace(expression)) node["expression"] = expression;
                        else ((JObject)node).Remove("expression");
                        break;
                    case "openPassage": EditorGUILayout.LabelField("解开后允许穿过这面墙"); break;
                    case "lua":
                        EditorGUILayout.LabelField("Lua 脚本");
                        node["script"] = EditorGUILayout.TextArea((string)node["script"] ?? "", GUILayout.MinHeight(52));
                        break;
                    case "setVisible": node["visible"] = EditorGUILayout.Toggle("动作 · 显示对象", (bool?)node["visible"] ?? false); break;
                    default: EditorGUILayout.LabelField(kind ?? "未知节点"); break;
                }
                foreach (var edge in (graph["edges"] as JArray ?? new JArray()).Where(e => (string)e["from"] == (string)node["id"]).ToList())
                {
                    var target = nodes.FirstOrDefault(n => (string)n["id"] == (string)edge["to"]);
                    GUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField((string)edge["out"] + " → " + ((string)target?["kind"] ?? "缺少目标"));
                    if (GUILayout.Button("断开", GUILayout.Width(48))) edge.Remove();
                    GUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
            if (nodes != null && nodes.Count > 0 && GUILayout.Button("追加 Lua 动作"))
            {
                string id = System.Guid.NewGuid().ToString("N");
                var last = nodes.Last;
                ((JArray)graph["edges"]).Add(new JObject { ["from"]=last["id"].DeepClone(), ["out"]=(string)last["kind"] == "condition" ? "true" : "out", ["to"]=id, ["in"]="in" });
                nodes.Add(new JObject { ["id"]=id, ["kind"]="lua", ["script"]="self.setVisible(false)" });
            }
            if (panel.Draft.ActiveId != roomId || panel.Draft.Revision != revision)
                EditorGUILayout.HelpBox("布局已变化，请返回后重新选择对象。", MessageType.Warning);
            else if (GUILayout.Button("应用行为并返回"))
            {
                panel.Run(() => panel.Draft.SetBehavior(cell, graph), "已保存对象行为");
                return panel.Draft.Revision == revision && panel.Status != "已保存对象行为";
            }
            return !GUILayout.Button("返回布局并放弃本页修改");
        }
    }
}
