/**
 * @file UX06BatchUpdateVerification.cs
 * @brief Run 通过公共草稿接口重选两墙并修改条件，逐字段对比预期图、边、选择及撤销重做状态。Check 记录逐项结果。
 */
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class UX06BatchUpdateVerification
    {
        static void Check(bool ok, string detail)
        {
            if (!ok) throw new InvalidOperationException(detail);
            Debug.Log("UX06BATCH PASS " + detail);
        }

        [MenuItem("Sokoban/验证/UX06-002 两墙重复批量更新")]
        public static void Run()
        {
            var draft = new LayoutPanel().Draft;
            var a = new Vector2Int(2, 0);
            var b = new Vector2Int(3, 0);
            var walls = new[] { a, b };
            Check(walls.All(p => RoomFile.Terrain(draft.Room, p) == '#'), "two wall cells");
            draft.Select(a);
            draft.Select(b, true);
            draft.SetSelectedName("北墙");
            Check(draft.TryGetSelectedName(out var name) && name == "北墙", "same name returns true and 北墙");
            draft.SetBehavior(b, ObjectBehavior.Template());
            draft.Select(a);
            draft.SetSelectedName("南墙");
            draft.Select(b, true);
            Check(draft.Selection.SetEquals(walls) && !draft.TryGetSelectedName(out name) && name == "", "mixed names return false and empty string");

            var before = draft.Snapshot();
            var expected = (JArray)draft.Room["events"].DeepClone();
            Check(expected.Count == 2 && walls.All(p => expected.Count(g => (string)g["ownerId"] == draft.OwnerAt(p)) == 1), "one bound graph per wall");
            foreach (var graph in expected)
            {
                var condition = graph["nodes"].Single(n => (string)n["kind"] == "condition");
                Check((bool?)condition["enabled"] == true, "initial condition enabled on " + graph["id"]);
                condition["enabled"] = false;
            }
            var primary = (JObject)draft.Room["events"].Single(g => (string)g["ownerId"] == draft.OwnerAt(b)).DeepClone();
            primary["nodes"].Single(n => (string)n["kind"] == "condition")["enabled"] = false;
            draft.SetBehavior(b, primary);
            var after = draft.Snapshot();
            Check(JToken.DeepEquals(expected, draft.Room["events"]), "both conditions disabled; graph count, owners, graph IDs, node IDs and edges preserved");
            Check(after["undo"].Count() == before["undo"].Count() + 1, "one history entry for both updates");
            draft.Undo();
            Check(JToken.DeepEquals(before["current"], draft.Snapshot()["current"]), "one undo restores both original graphs and selection");
            draft.Redo();
            Check(JToken.DeepEquals(after["current"], draft.Snapshot()["current"]), "one redo restores both modified graphs and selection");
            var secondary = (JObject)draft.Room["events"].Single(g => (string)g["ownerId"] == draft.OwnerAt(a)).DeepClone();
            var nodes = (JArray)secondary["nodes"];
            var first = nodes.First;
            first.Remove();
            nodes.Add(first);
            draft.Select(a);
            draft.SetBehavior(a,secondary);
            draft.Select(b,true);
            primary["nodes"].Single(n => (string)n["kind"] == "condition")["enabled"] = true;
            draft.SetBehavior(b,primary);
            var updated = draft.Room["events"].Single(g => (string)g["ownerId"] == draft.OwnerAt(a));
            Check(nodes.All(n => updated["nodes"].Any(other => (string)other["kind"] == (string)n["kind"] && (string)other["id"] == (string)n["id"])), "independently reordered nodes keep their IDs after batch update");
        }
    }
}
