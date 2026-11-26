/**
 * @file BrushParityVerification.cs
 * @brief Run 对三个正式房间与 Wall、Floor、Erase 三种工具，生成单格、横线、跨对象线、含门格线、外扩线、越界很远线等笔划，分别交给旧 RoomDraft.Brush 与 Core BrushPlanner.Preflight 判定并记录差异清单；这是差异清单而非一致性断言，只有脚本异常才以退出码 1 结束。
 * Run：批处理入口，-executeMethod Sokoban.Editor.BrushParityVerification.Run；逐房间、逐笔划、逐工具调用 Judge，汇总后调用 Report，并以 EditorApplication.Exit 返回退出码。
 * Strokes：按房间实际的空地板、墙、箱子、门位置生成笔划清单，每条笔划带类型名与格集合。
 * Row、Column：生成一行、一列格集合的小工具。
 * Judge：对同一笔划在新副本上分别执行旧侧与新侧，按新旧是否接受归类，旧侧被拒绝却已改动记为部分提交，两侧都接受时再比较结果地形。
 * OldRun：构造 new RoomDraft(json) 副本，调用公开的 Brush(cells, tool)，以返回文案为空表示接受，并用 JToken.DeepEquals 判断房间是否被改动。
 * NewRun：经 RoomFile.ToLegacyData 与 LegacyRoomAdapter.ToDraft 转成 Core 草稿后调用 BrushPlanner.Preflight；BrushPlan.Result 是 internal，故接受时经 CreateCommand 与 DraftHolder 应用命令，从 holder.Draft 取结果。
 * Category：把旧房间的地形字符（墙、空洞、其余视为地板）映射为 Core 的地形类别 ID，供地形比较使用。
 * TerrainDiff：两侧都接受时比较尺寸与每格墙、地板、空洞类别，门格等 Core 其他类别跳过。
 * IsDiff：一条记录属于差异的条件，即仅旧接受、仅新接受、两侧都接受但地形不同，或两侧都拒绝而旧版已部分提交。
 * DiffLabel：给差异记录取简短的归类名，用于样例行。
 * Report：按工具与笔划类型汇总四类结果计数、旧版部分提交数、地形差异数，并列出前 20 条差异样例，全部以 BRUSH-DIFF 开头。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;
using SC = Sokoban.Core;

namespace Sokoban.Editor
{
    public static class BrushParityVerification
    {
        static readonly string[] Rooms = { "warehouse", "factory", "forest" };
        static readonly string[] Outcomes = { "都接受", "仅旧接受", "仅新接受", "都拒绝" };
        static readonly ToolPair[] Tools =
        {
            new ToolPair { Label = "Wall", Old = RoomTool.Wall, New = SC.BrushTool.Wall },
            new ToolPair { Label = "Floor", Old = RoomTool.Floor, New = SC.BrushTool.Floor },
            new ToolPair { Label = "Erase", Old = RoomTool.Erase, New = SC.BrushTool.Erase },
        };

        sealed class ToolPair { public string Label; public RoomTool Old; public SC.BrushTool New; }
        sealed class Stroke { public string Kind; public Vector2Int[] Cells; }
        sealed class Record
        {
            public string Room, Tool, Kind, OldText, NewText, TerrainNote;
            public int Outcome, CellCount;
            public bool OldPartial;
        }

        static Vector2Int[] Row(int y, int x0, int x1) => Enumerable.Range(x0, x1 - x0 + 1).Select(x => new Vector2Int(x, y)).ToArray();

        static Vector2Int[] Column(int x, int y0, int y1) => Enumerable.Range(y0, y1 - y0 + 1).Select(y => new Vector2Int(x, y)).ToArray();

        static IEnumerable<Stroke> Strokes(JObject room)
        {
            var size = RoomFile.Size(room);
            var taken = new HashSet<Vector2Int>(RoomFile.Objects(room).Select(RoomFile.At));
            foreach (var door in room["doors"]) taken.Add(RoomFile.At(door) + RoomFile.Inward(door));
            Vector2Int? free = null, wall = null;
            for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                {
                    var cell = new Vector2Int(x, y);
                    if (!free.HasValue && RoomFile.Floor(room, cell) && !taken.Contains(cell)) free = cell;
                    if (!wall.HasValue && RoomFile.Terrain(room, cell) == '#') wall = cell;
                }
            if (free.HasValue) yield return new Stroke { Kind = "单格空地板", Cells = new[] { free.Value } };
            if (wall.HasValue) yield return new Stroke { Kind = "单格墙", Cells = new[] { wall.Value } };
            yield return new Stroke { Kind = "房内横线", Cells = Row(size.y / 2, 0, size.x - 1) };
            var box = room["boxes"].FirstOrDefault();
            if (box != null) yield return new Stroke { Kind = "跨对象格横线", Cells = Row(RoomFile.At(box).y, 0, size.x - 1) };
            var gate = room["doors"].FirstOrDefault();
            if (gate != null) yield return new Stroke { Kind = "含门格横线", Cells = Row(RoomFile.At(gate).y, 0, size.x - 1) };
            if (gate != null) yield return new Stroke { Kind = "含门内侧格竖线", Cells = Column((RoomFile.At(gate) + RoomFile.Inward(gate)).x, 0, size.y - 1) };
            yield return new Stroke { Kind = "向下外扩一格横线", Cells = Row(-1, 0, size.x - 1) };
            yield return new Stroke { Kind = "向右外扩一格竖线", Cells = Column(size.x, 0, size.y - 1) };
            yield return new Stroke { Kind = "越界很远单格", Cells = new[] { new Vector2Int(size.x + 40, size.y + 40) } };
            yield return new Stroke { Kind = "越界很远横线", Cells = Row(-40, 0, 3) };
        }

        static string OldRun(string json, Stroke stroke, ToolPair tool, out bool accepted, out bool changed, out JObject after)
        {
            var draft = new RoomDraft(json);
            var before = (JObject)draft.Room.DeepClone();
            string message;
            try { message = draft.Brush(stroke.Cells, tool.Old); }
            catch (Exception ex) { message = "异常 " + ex.GetType().Name + "：" + ex.Message; }
            after = draft.Room;
            changed = !JToken.DeepEquals(before, draft.Room);
            accepted = message == null;
            return message;
        }

        static string NewRun(JObject room, Stroke stroke, ToolPair tool, out bool accepted, out SC.RoomDraftModel after)
        {
            var draft = SC.LegacyRoomAdapter.ToDraft(RoomFile.ToLegacyData(room));
            var plan = SC.BrushPlanner.Preflight(draft, stroke.Cells.Select(p => new SC.Cell(p.x, p.y)), tool.New);
            accepted = plan.Error == null;
            after = draft;
            if (!accepted) return plan.Error;
            if (plan.NoChange) return "接受但无改动";
            var holder = new SC.DraftHolder(draft);
            plan.CreateCommand(holder).Apply();
            after = holder.Draft;
            return null;
        }

        static string Category(char tile) => tile == '#' ? SC.TerrainCategories.Wall : tile == RoomFile.Void ? SC.TerrainCategories.Void : SC.TerrainCategories.Floor;

        static string TerrainDiff(JObject oldRoom, SC.RoomDraftModel newDraft)
        {
            var size = RoomFile.Size(oldRoom);
            var room = newDraft.Room;
            if (size.x != room.Width || size.y != room.Height) return "尺寸不同 旧=" + size.x + "×" + size.y + " 新=" + room.Width + "×" + room.Height;
            int count = 0;
            string first = null;
            for (int y = 0; y < size.y; y++)
                for (int x = 0; x < size.x; x++)
                {
                    string now = room.TerrainAt(new SC.Cell(x, y)).CategoryId;
                    if (now == SC.TerrainCategories.Door) continue;
                    string was = Category(RoomFile.Terrain(oldRoom, new Vector2Int(x, y)));
                    if (was == now) continue;
                    count++;
                    if (first == null) first = "(" + x + "," + y + ") 旧=" + was + " 新=" + now;
                }
            return count == 0 ? null : "地形差异格数=" + count + " 首格 " + first;
        }

        static Record Judge(string roomName, string json, JObject room, Stroke stroke, ToolPair tool)
        {
            string oldText = OldRun(json, stroke, tool, out bool oldAccepted, out bool changed, out var oldAfter);
            string newText = NewRun(room, stroke, tool, out bool newAccepted, out var newAfter);
            int outcome = oldAccepted && newAccepted ? 0 : oldAccepted ? 1 : newAccepted ? 2 : 3;
            return new Record
            {
                Room = roomName,
                Tool = tool.Label,
                Kind = stroke.Kind,
                CellCount = stroke.Cells.Length,
                Outcome = outcome,
                OldText = oldText ?? "接受",
                NewText = newText ?? "接受",
                OldPartial = !oldAccepted && changed,
                TerrainNote = outcome == 0 ? TerrainDiff(oldAfter, newAfter) : null,
            };
        }

        static bool IsDiff(Record r) => r.Outcome == 1 || r.Outcome == 2 || r.TerrainNote != null || r.OldPartial;

        static string DiffLabel(Record r) => r.Outcome == 1 ? "仅旧接受" : r.Outcome == 2 ? "仅新接受" : r.TerrainNote != null ? "都接受但地形不同" : "都拒绝但旧版部分提交";

        static void Report(List<Record> records)
        {
            var total = new int[4];
            foreach (var r in records) total[r.Outcome]++;
            Debug.Log("BRUSH-DIFF 总计 笔划判定数=" + records.Count + " " + string.Join(" ", Outcomes.Select((o, i) => o + "=" + total[i])) + " 旧版部分提交=" + records.Count(r => r.OldPartial) + " 都接受但地形不同=" + records.Count(r => r.TerrainNote != null));
            foreach (var group in records.GroupBy(r => r.Tool + " / " + r.Kind))
            {
                var counts = new int[4];
                foreach (var r in group) counts[r.Outcome]++;
                Debug.Log("BRUSH-DIFF 分类 " + group.Key + " 次数=" + group.Count() + " " + string.Join(" ", Outcomes.Select((o, i) => o + "=" + counts[i])) + " 旧版部分提交=" + group.Count(r => r.OldPartial) + " 地形不同=" + group.Count(r => r.TerrainNote != null));
            }
            foreach (var group in records.GroupBy(r => r.Room))
            {
                var counts = new int[4];
                foreach (var r in group) counts[r.Outcome]++;
                Debug.Log("BRUSH-DIFF 房间 " + group.Key + " " + string.Join(" ", Outcomes.Select((o, i) => o + "=" + counts[i])));
            }
            var diffs = records.Where(IsDiff).ToList();
            Debug.Log("BRUSH-DIFF 差异样例 共 " + diffs.Count + " 条，列出前 " + Math.Min(20, diffs.Count) + " 条");
            foreach (var r in diffs.Take(20))
                Debug.Log("BRUSH-DIFF 样例 " + DiffLabel(r) + " " + r.Room + " " + r.Tool + " " + r.Kind + " 格数=" + r.CellCount + " 旧文案=" + r.OldText + " 新Error=" + r.NewText + (r.TerrainNote != null ? " " + r.TerrainNote : ""));
        }

        public static void Run()
        {
            var failures = new List<string>();
            var records = new List<Record>();
            try
            {
                foreach (var name in Rooms)
                {
                    string json = File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "World/" + name + ".room.json"));
                    var room = RoomFile.Parse(json, true);
                    foreach (var stroke in Strokes(room))
                        foreach (var tool in Tools)
                            try { records.Add(Judge(name, json, room, stroke, tool)); }
                            catch (Exception ex)
                            {
                                Debug.LogException(ex);
                                failures.Add(name + " " + tool.Label + " " + stroke.Kind + " 异常：" + ex.Message);
                            }
                }
                Report(records);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                failures.Add("异常：" + ex.Message);
            }
            foreach (var line in failures) Debug.LogError("BRUSH-DIFF FAIL " + line);
            Debug.Log("BRUSH-DIFF 结束 记录数=" + records.Count + " 异常数=" + failures.Count);
            EditorApplication.Exit(failures.Count == 0 ? 0 : 1);
        }
    }
}
