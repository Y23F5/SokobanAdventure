/**
 * @file TerrainExpansionVerification.cs
 * @brief Run 从菜单或批处理逐例执行共享命令并汇总失败。Fixture 构造带地形身份与独立恢复格的世界。
 * Expansion 核对尺寸、原点、旧格与身份；Conflict 检查冲突整笔回滚；History 检查草稿与双向历史；
 * Erase 检查对象、地形及受保护格。Require 报告断言，Paint 调用共享笔刷并检查返回值。
 */
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class TerrainExpansionVerification
    {
        static readonly Vector2Int Start = new Vector2Int(10, 10);
        static readonly Vector2Int BoundCell = new Vector2Int(2, 4);

        static RoomDraft Fixture()
        {
            var room = JObject.Parse(Resources.Load<TextAsset>("DefaultRoom").text);
            room["boxes"][0]["recoveryCell"] = new JArray(3, 4);
            room["terrainObjects"] = new JArray(new JObject { ["id"] = "floor-a", ["cell"] = RoomFile.Point(BoundCell) });
            room["events"] = new JArray(new JObject { ["id"] = "graph-a", ["ownerId"] = "floor-a", ["nodes"] = new JArray(), ["edges"] = new JArray() });
            var draft = new RoomDraft(room.ToString());
            draft.BeginWorld();
            draft.PlaceRoom(Start, false);
            return draft;
        }

        static void Require(bool ok, string detail)
        {
            if (!ok) throw new InvalidOperationException(detail);
        }

        static void Paint(RoomDraft draft, Vector2Int point, RoomTool tool)
        {
            var issue = draft.Brush(new[] { point }, tool);
            Require(issue == null, $"{point} {tool} {issue}");
        }

        static void Expansion(Vector2Int point, Vector2Int shift, Vector2Int growth, RoomTool tool)
        {
            var draft = Fixture();
            var before = (JObject)draft.Room.DeepClone();
            var size = RoomFile.Size(before);
            Paint(draft, point, tool);
            Require(draft.Origin == Start + shift && RoomFile.Size(draft.Room) == size + growth, "扩房原点或尺寸错误");
            for (int y = 0; y < size.y + growth.y; y++)
            for (int x = 0; x < size.x + growth.x; x++)
            {
                var local = new Vector2Int(x, y);
                var old = local + shift;
                char expected = old == point ? (tool == RoomTool.Wall ? '#' : '.') : RoomFile.Terrain(before, old);
                Require(RoomFile.Terrain(draft.Room, local) == expected, $"地形行偏移 {local}");
            }
            foreach (var item in RoomFile.Objects(before).Concat(before["terrainObjects"]))
            {
                var id = (string)item["id"];
                var actual = RoomFile.Objects(draft.Room).Concat(draft.Room["terrainObjects"]).Single(o => (string)o["id"] == id);
                Require(draft.Origin + RoomFile.At(actual) == Start + RoomFile.At(item), id + " 世界格改变");
            }
            Require(draft.Origin + RoomFile.Cell(draft.Find("box-a")["recoveryCell"]) == Start + new Vector2Int(3, 4), "恢复格漂移");
            Require(JToken.DeepEquals(draft.Room["events"], before["events"]), "地形绑定改变");
        }

        static void Conflict(bool overlap)
        {
            var draft = Fixture();
            var point = new Vector2Int(-11, 2);
            if (overlap)
            {
                string first = draft.ActiveId;
                var other = (JObject)draft.Room.DeepClone();
                other["levelId"] = "neighbor";
                draft.Catalog["neighbor"] = other;
                draft.UseRoom("neighbor");
                draft.PlaceRoom(Start + new Vector2Int(7, 0), false);
                draft.UseRoom(first);
                point = new Vector2Int(7, 2);
            }
            var before = draft.Snapshot();
            var issue = draft.Brush(new[] { BoundCell, point }, RoomTool.Wall);
            Require(issue != null && issue.Contains(overlap ? "重叠" : "边界"), "未报告预期冲突 " + issue);
            Require(JToken.DeepEquals(before, draft.Snapshot()), "冲突改变工作区或历史");
        }

        static void History()
        {
            var draft = Fixture();
            var before = draft.Snapshot()["current"];
            string issue = draft.Brush(new[] { new Vector2Int(-1, 2), new Vector2Int(1, -1) }, RoomTool.Floor);
            Require(issue == null && draft.Origin == Start - Vector2Int.one, "西南复合笔失败 " + issue);
            var after = draft.Snapshot()["current"];
            draft.Undo();
            Require(JToken.DeepEquals(before, draft.Snapshot()["current"]), "一笔撤销未恢复房间与世界");
            var snapshot = draft.Snapshot();
            var restored = Fixture();
            restored.Recover(JObject.Parse(snapshot.ToString()));
            var recovered = JObject.Parse(restored.Snapshot().ToString());
            restored.Redo();
            Require(JToken.DeepEquals(JToken.Parse(after.ToString()), JToken.Parse(restored.Snapshot()["current"].ToString())), "草稿重做丢失扩房结果");
            restored.Undo();
            Require(JToken.DeepEquals(JToken.Parse(before.ToString()), JToken.Parse(restored.Snapshot()["current"].ToString())), "草稿再次撤销失败");
            Require(JToken.DeepEquals(JObject.Parse(snapshot.ToString()), recovered), "序列化草稿改变");
        }

        static void Erase(bool bound)
        {
            var draft = Fixture();
            if (bound)
            {
                var before = draft.Snapshot()["current"];
                Paint(draft, BoundCell, RoomTool.Erase);
                Require(RoomFile.Terrain(draft.Room, BoundCell) == RoomFile.Void, "擦除后仍有地形");
                bool retainedAfterErase = (draft.Room["terrainObjects"] as JArray ?? new JArray()).Any(o => (string)o["id"] == "floor-a");
                draft.Undo();
                Require(JToken.DeepEquals(before, draft.Snapshot()["current"]), "撤销擦除未恢复原身份");
                draft.Redo();
                Paint(draft, BoundCell, RoomTool.Floor);
                bool retainedAfterRepaint = (draft.Room["terrainObjects"] as JArray ?? new JArray()).Any(o => (string)o["id"] == "floor-a");
                Require(!retainedAfterErase && !retainedAfterRepaint, $"旧身份 floor-a 存在于空格={retainedAfterErase}，重绘后={retainedAfterRepaint}");
                return;
            }
            var initial = draft.Snapshot();
            var blocked = draft.Brush(new[] { new Vector2Int(3, 4) }, RoomTool.Erase);
            Require(blocked != null && JToken.DeepEquals(initial, draft.Snapshot()), "擦掉箱子恢复格");
            var cell = RoomFile.At(draft.Find("box-a"));
            Paint(draft, cell, RoomTool.Erase);
            Require(draft.Find("box-a") == null && RoomFile.Floor(draft.Room, cell), "对象与脚下地板未分层擦除");
            Paint(draft, cell, RoomTool.Erase);
            Require(RoomFile.Terrain(draft.Room, cell) == RoomFile.Void, "第二笔没有擦除地板");
        }

        [MenuItem("Sokoban/验证/WS-05A 共享地形与扩建")]
        public static void Run()
        {
            int failed = 0;
            int total = 0;
            void Test(string name, Action check)
            {
                total++;
                try { check(); Debug.Log("WS05A PASS " + name); }
                catch (Exception ex) { failed++; Debug.LogError("WS05A FAIL " + name + " " + ex); }
            }
            Test("east", () => Expansion(new Vector2Int(7, 1), Vector2Int.zero, Vector2Int.right, RoomTool.Floor));
            Test("west", () => Expansion(new Vector2Int(-1, 1), Vector2Int.left, Vector2Int.right, RoomTool.Wall));
            Test("north", () => Expansion(new Vector2Int(1, 7), Vector2Int.zero, Vector2Int.up, RoomTool.Wall));
            Test("south", () => Expansion(new Vector2Int(1, -1), Vector2Int.down, Vector2Int.up, RoomTool.Floor));
            Test("world-boundary", () => Conflict(false));
            Test("world-overlap", () => Conflict(true));
            Test("history-draft", History);
            Test("erase-object-recovery", () => Erase(false));
            Test("erase-bound-terrain", () => Erase(true));
            Debug.Log($"WS05A RESULT {total - failed}/{total}");
            Require(failed == 0, $"WS05A {failed} failed");
        }
    }
}
