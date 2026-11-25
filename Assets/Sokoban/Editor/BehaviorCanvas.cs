/**
 * @file BehaviorCanvas.cs
 * @brief Draw 绘制可拖动节点及其连接。Port 从输出端选择连线起点，再点目标输入端建立连接。Add 创建带默认参数的节点。
 */
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
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
        static readonly string[] Kinds = { "roomSolved", "condition", "setVisible", "lua", "openPassage", "setState" };
        static readonly string[] Labels = { "房间解开", "条件", "显示对象", "Lua", "开放墙格", "切换物件状态" };
        int selectedKind;
        static Rect Position(JToken node, int index) => new Rect((float?)node["canvasX"] ?? 12 + 148 * (index % 4), (float?)node["canvasY"] ?? 14 + 88 * (index / 4), Width, Height);
        internal void Draw(JObject graph)
        {
            var nodes = (JArray)graph["nodes"];
            var edges = (JArray)graph["edges"];
            if (nodes == null || edges == null) return;
            GUILayout.BeginHorizontal();
            selectedKind = EditorGUILayout.Popup(selectedKind, Labels);
            if (GUILayout.Button("添加节点")) Add(nodes);
            GUILayout.EndHorizontal();
            var viewport = GUILayoutUtility.GetRect(80, 218, GUILayout.ExpandWidth(true));
            var bounds = new Rect(0, 0, 640, 310);
            for (int i=0; i<nodes.Count; i++)
            {
                Rect rect = Position(nodes[i], i);
                bounds.width = Mathf.Max(bounds.width, rect.xMax + 50);
                bounds.height = Mathf.Max(bounds.height, rect.yMax + 40);
            }
            scroll = GUI.BeginScrollView(viewport, scroll, bounds);
            if (Event.current.type == EventType.Repaint)
                foreach (var edge in edges)
                {
                    var from = nodes.FirstOrDefault(n => (string)n["id"] == (string)edge["from"]);
                    var to = nodes.FirstOrDefault(n => (string)n["id"] == (string)edge["to"]);
                    if (from == null || to == null) continue;
                    var a = Position(from, nodes.IndexOf(from)); var b = Position(to, nodes.IndexOf(to));
                    var start = new Vector3(a.xMax, a.center.y); var end = new Vector3(b.xMin, b.center.y);
                    Handles.DrawBezier(start, end, start + Vector3.right * 36, end + Vector3.left * 36, Color.cyan, null, 2);
                }
            for (int i=0; i<nodes.Count; i++)
            {
                var node = nodes[i]; string id=(string)node["id"], kind=(string)node["kind"];
                var rect=Position(node,i);
                GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
                var title = new Rect(rect.x+4,rect.y+3,Width-30,22);
                int type=Array.IndexOf(Kinds,kind);
                GUI.Label(title,type<0?kind:Labels[type],EditorStyles.boldLabel);
                var ev=Event.current;
                if (ev.type==EventType.MouseDown && ev.button==0 && title.Contains(ev.mousePosition)) { drag=id;offset=ev.mousePosition-rect.position;ev.Use(); }
                if (ev.type==EventType.MouseDrag && drag==id) { var p=ev.mousePosition-offset;node["canvasX"]=Mathf.Max(0,p.x);node["canvasY"]=Mathf.Max(0,p.y);ev.Use(); }
                if (ev.rawType==EventType.MouseUp && drag==id) drag=null;
                if (GUI.Button(new Rect(rect.xMax-25,rect.y+3,22,20),"×"))
                {
                    foreach(var edge in edges.Where(e=>(string)e["from"]==id || (string)e["to"]==id).ToList()) edge.Remove();
                    node.Remove(); if(source==id) source=null; break;
                }
                if (kind!="roomSolved" && GUI.Button(new Rect(rect.x+3,rect.y+31,34,22),"入") && source!=null && source!=id)
                {
                    if (!edges.Any(e=>(string)e["from"]==source && (string)e["out"]==outlet && (string)e["to"]==id))
                        edges.Add(new JObject { ["from"]=source,["out"]=outlet,["to"]=id,["in"]="in" });
                    source=null;
                }
                if (kind=="condition") { Port(rect,id,"true","真",44); Port(rect,id,"false","假",88); }
                else Port(rect,id,"out","出",88);
            }
            GUI.EndScrollView();
            if (source!=null && GUILayout.Button("取消连线")) source=null;
        }
        void Port(Rect rect,string id,string port,string title,float x)
        {
            if (GUI.Button(new Rect(rect.x+x,rect.y+31,38,22),title)) { source=id;outlet=port; }
        }
        void Add(JArray nodes)
        {
            string kind=Kinds[selectedKind];
            var node=new JObject { ["id"]=Guid.NewGuid().ToString("N"),["kind"]=kind };
            if (kind=="condition") node["enabled"]=true;
            if (kind=="setVisible") node["visible"]=false;
            if (kind=="lua") node["script"]="self.setVisible(false)";
            nodes.Add(node);
        }
    }
}
