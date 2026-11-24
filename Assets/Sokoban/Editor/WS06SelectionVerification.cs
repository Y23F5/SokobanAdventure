/**
 * @file WS06SelectionVerification.cs
 * @brief Run 检查共享选择与批量命令，RecoverHistory 重读草稿并回放历史；Legacy 重读 WS-01 交换房与 WS-02 旧草稿。
 * Check 输出逐项断言。Graph 按格子稳定身份取图；Edges 将图内端口与节点还原为可比较的连接。
 */
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class WS06SelectionVerification
    {
        static void Check(bool passed, string detail)
        {
            if (!passed) throw new InvalidOperationException(detail);
            Debug.Log("WS06SELECT PASS " + detail);
        }

        static JObject Graph(RoomDraft draft, Vector2Int point) => (JObject)draft.Room["events"].Single(g => (string)g["ownerId"] == draft.OwnerAt(point));

        static string[] Edges(JObject graph)
        {
            var nodes = graph["nodes"].ToDictionary(n => (string)n["id"], n => (string)n["kind"]);
            return graph["edges"].Select(e => nodes[(string)e["from"]] + "." + e["out"] + "->" + nodes[(string)e["to"]] + "." + e["in"]).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        }

        static void RecoverHistory(RoomDraft draft, JToken before, JToken after, string path)
        {
            var snapshot = draft.Snapshot();
            File.WriteAllText(path, snapshot.ToString());
            var restored = new LayoutPanel().Draft;
            restored.Recover(JObject.Parse(File.ReadAllText(path)));
            Check(restored.Selection.SetEquals(draft.Selection) && restored.SelectedCell == draft.SelectedCell && JToken.DeepEquals(restored.Room, draft.Room), "磁盘草稿恢复两格选择、主格及绑定图");
            restored.Undo();
            Check(JToken.DeepEquals(before, JObject.Parse(restored.Snapshot().ToString())["current"]), "恢复后一次撤销还原绑定前全部状态");
            restored.Redo();
            Check(JToken.DeepEquals(after, JObject.Parse(restored.Snapshot().ToString())["current"]), "恢复后一次重做还原两图及选择");
        }

        static void Legacy()
        {
            var draft = new LayoutPanel().Draft;
            foreach (var file in new[]{"A.json","B.json"})
            {
                var path = Path.Combine("Docs/验收证据/WS-01",file);
                draft.Open(path,false);
                Check(draft.Selection.Count == 0 && !draft.SelectedCell.HasValue && JToken.DeepEquals(draft.Room,JObject.Parse(File.ReadAllText(path))), "WS-01 历史交换房重开 " + file);
            }
            var old = JObject.Parse(File.ReadAllText("Docs/验收证据/WS-02/20261123-135523/test-ws02.json"));
            var current = old["current"];
            Check(current["selection"] == null && current["cell"] is JArray, "WS-02 旧槽无多选字段但有选中格");
            draft.Recover(old);
            var point = RoomFile.Cell(current["cell"]);
            Check(draft.Selection.SetEquals(new[]{point}) && draft.Selected == (string)current["selected"] && draft.SelectedCell == point, "旧单选回填 " + point);
            var room = draft.Room.DeepClone();
            draft.Undo();
            Check(draft.Selection.Count == 0 && draft.SelectedCell == null && !JToken.DeepEquals(room,draft.Room), "旧历史撤销到无选中格");
            draft.Redo();
            Check(draft.Selection.SetEquals(new[]{point}) && JToken.DeepEquals(room,draft.Room), "旧历史重做还原房间与单选");
        }

        [MenuItem("Sokoban/验证/WS-06 多选与批量行为")]
        public static void Run()
        {
            var draft = new LayoutPanel().Draft;
            var a = new Vector2Int(2,0);
            var b = new Vector2Int(3,0);
            draft.Select(a); draft.Select(b,true);
            Check(draft.Selection.SetEquals(new[]{a,b}) && draft.SelectedCell == b, "追加第二格并设为主格");
            draft.Select(b,true);
            Check(draft.Selection.SetEquals(new[]{a}) && draft.SelectedCell == a, "再次追加同格取消选择，主格回退");
            draft.Select(a,true);
            Check(draft.Selection.Count == 0 && draft.SelectedCell == null, "取消剩余单格清空选择");
            draft.Select(a); draft.Select(b,true);
            var prior = JObject.Parse(draft.Snapshot().ToString());
            draft.SetBehavior(b,ObjectBehavior.Template());
            var applied = JObject.Parse(draft.Snapshot().ToString());
            Check(applied["undo"].Count() == prior["undo"].Count()+1 && draft.Selection.SetEquals(new[]{a,b}) && draft.SelectedCell == b, "一次批量绑定只增加一个历史项，保留选择");
            var first = Graph(draft,a); var second = Graph(draft,b);
            Check(draft.OwnerAt(a)!=draft.OwnerAt(b) && draft.Room["events"].Count()==2 && (string)first["id"]!=(string)second["id"] && !first["nodes"].Select(n=>(string)n["id"]).Intersect(second["nodes"].Select(n=>(string)n["id"])).Any() && Edges(first).SequenceEqual(Edges(second)) && Edges(first).Length==2, "两格拥有独立身份、图和节点，两条边均指向各自图内节点");
            draft.Undo(); Check(JToken.DeepEquals(prior["current"],JObject.Parse(draft.Snapshot().ToString())["current"]), "一步撤销两图和新建地形身份");
            draft.Redo(); Check(JToken.DeepEquals(applied["current"],JObject.Parse(draft.Snapshot().ToString())["current"]), "一步重做恢复原图、原 ID 与选择");
            var folder = Environment.GetEnvironmentVariable("WS06_SELECTION_EVIDENCE") ?? Path.GetFullPath("Logs/WS06Selection");
            Directory.CreateDirectory(folder);
            RecoverHistory(draft,prior["current"],applied["current"],Path.Combine(folder,"selection-draft.json"));
            draft.Select(a);
            var other = Graph(draft,b).DeepClone(); var changed = (JObject)Graph(draft,a).DeepClone();
            changed["nodes"].Single(n=>(string)n["kind"]=="condition")["enabled"]=false;
            draft.SetBehavior(a,changed);
            Check(draft.Selection.SetEquals(new[]{a}) && draft.Room["events"].Count()==2 && JToken.DeepEquals(changed,Graph(draft,a)) && JToken.DeepEquals(other,Graph(draft,b)), "切回单选编辑一图，另一图逐字段一致");
            Legacy();
        }
    }
}
