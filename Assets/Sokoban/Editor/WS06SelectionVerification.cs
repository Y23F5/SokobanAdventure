/**
 * @file WS06SelectionVerification.cs
 * @brief 共享选择与批量行为绑定的编辑器验证，全部走类型化草稿，行为图读自 Model.Graphs。
 * Check(passed, detail)：断言失败抛 InvalidOperationException，通过则输出一行日志。
 * Graph(draft, cell)：按格子的稳定所有者 ID 在 Model.Graphs 中取唯一的图。
 * Edges(graph)：把图内连线还原为“起点类型.端口->终点类型.端口”的有序字符串，便于跨图比较连接结构。
 * Encoded(draft)：以 RoomCodec.Encode 序列化当前房间模型，用于逐字比较撤销、重做与恢复前后的全部数据。
 * RecoverDraft(draft, path)：把 Snapshot 字符串写入磁盘再读回新草稿 Recover，要求选择已清空，房间编码与绑定图的 ID、所有者和连接均一致。
 * Run()：菜单入口，依次检查追加与取消选择、一次批量绑定的独立身份与单步撤销重做、磁盘恢复、切回单选只改一图。
 */
using System;
using System.IO;
using System.Linq;
using Sokoban.Core;
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

        static BehaviorGraph Graph(RoomDraft draft, Cell cell) => draft.Model.Graphs.Single(g => g.OwnerId == draft.OwnerAt(cell));

        static string[] Edges(BehaviorGraph graph)
        {
            var kinds = graph.Nodes.ToDictionary(n => n.Id, n => n.Kind);
            return graph.Edges.Select(e => kinds[e.FromNode] + "." + e.FromPort + "->" + kinds[e.ToNode] + "." + e.ToPort).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        }

        static string Encoded(RoomDraft draft) => RoomCodec.Encode(draft.Model);

        static void RecoverDraft(RoomDraft draft, string path)
        {
            File.WriteAllText(path, draft.Snapshot());
            var restored = new LayoutPanel().Draft;
            restored.Recover(File.ReadAllText(path));
            Check(restored.Selection.Count == 0 && !restored.SelectedCell.HasValue, "磁盘草稿恢复后选择与主格清空");
            Check(Encoded(restored) == Encoded(draft) && restored.Model.Graphs.Count == draft.Model.Graphs.Count
                && restored.Model.Graphs.All(g => draft.Model.Graphs.Any(o => o.Id == g.Id && o.OwnerId == g.OwnerId && Edges(o).SequenceEqual(Edges(g)))), "磁盘草稿恢复房间编码与两张绑定图的 ID、所有者和连接");
        }

        [MenuItem("Sokoban/验证/WS-06 多选与批量行为")]
        public static void Run()
        {
            var draft = new LayoutPanel().Draft;
            var a = new Cell(2, 0);
            var b = new Cell(3, 0);
            draft.Select(a); draft.Select(b, true);
            Check(draft.Selection.SetEquals(new[] { a, b }) && draft.SelectedCell == b, "追加第二格并设为主格");
            draft.Select(b, true);
            Check(draft.Selection.SetEquals(new[] { a }) && draft.SelectedCell == a, "再次追加同格取消选择，主格回退");
            draft.Select(a, true);
            Check(draft.Selection.Count == 0 && draft.SelectedCell == null, "取消剩余单格清空选择");
            draft.Select(a); draft.Select(b, true);
            string prior = Encoded(draft);
            int revision = draft.Revision;
            draft.SetBehavior(b, ObjectBehavior.Template());
            string applied = Encoded(draft);
            Check(draft.Revision > revision && draft.Selection.SetEquals(new[] { a, b }) && draft.SelectedCell == b, "批量绑定推进 Revision，保留选择与主格");
            var first = Graph(draft, a);
            var second = Graph(draft, b);
            Check(draft.OwnerAt(a) != draft.OwnerAt(b) && draft.Model.Graphs.Count == 2 && first.Id != second.Id
                && !first.Nodes.Select(n => n.Id).Intersect(second.Nodes.Select(n => n.Id)).Any()
                && Edges(first).SequenceEqual(Edges(second)) && Edges(first).Length == 2, "两格拥有独立身份、图和节点，两条边均指向各自图内节点");
            draft.Undo();
            Check(Encoded(draft) == prior && draft.Model.Graphs.Count == 0 && draft.Selection.Count == 0, "一步撤销两图和新建地形身份，选择清空");
            draft.Redo();
            Check(Encoded(draft) == applied && draft.Model.Graphs.Count == 2, "一步重做恢复两图、原 ID 与地形身份");
            var folder = Environment.GetEnvironmentVariable("WS06_SELECTION_EVIDENCE") ?? Path.GetFullPath("Logs/WS06Selection");
            Directory.CreateDirectory(folder);
            RecoverDraft(draft, Path.Combine(folder, "selection-draft.json"));
            draft.Select(a);
            var other = Graph(draft, b);
            var source = Graph(draft, a);
            var nodes = source.Nodes.Select(n => n.Kind == ObjectBehavior.Condition ? ObjectBehavior.WithParameter(n, "enabled", "false") : n).ToList();
            var changed = new BehaviorGraph(source.Id, source.OwnerId, nodes, source.Edges);
            draft.SetBehavior(a, changed);
            var edited = Graph(draft, a);
            var condition = edited.Nodes.Single(n => n.Kind == ObjectBehavior.Condition);
            Check(draft.Selection.Contains(a) && draft.Model.Graphs.Count == 2 && ObjectBehavior.Parameter(condition, "enabled") == "false"
                && ReferenceEquals(other, Graph(draft, b)) && Edges(Graph(draft, b)).SequenceEqual(Edges(other)), "切回单选编辑一图，另一图保持同一对象且连接不变");
        }
    }
}
