/**
 * @file ObjectBehavior.cs
 * @brief 行为图的编辑入口与规则查询，数据一律读写 Core.BehaviorGraph，触发器用 TriggerIds，节点类型由 Registries.BehaviorNodes 登记；不再有 openPassage。
 * RoomDraft.OwnerAt(cell)：返回格上对象的 ID，没有对象时返回地形实体 ID，都没有时为空。
 * RoomDraft.GraphOf(cell)：返回该格所有者的第一张行为图（含对象模板实例从模板解析出的图），没有时为空。
 * RoomDraft.SetBehavior(cell, graph)：把图写给该格所有者，所选的其他格各自获得独立 ID 的副本；地形格缺实体 ID 时同一次提交里生成并保留原外观；
 * 整次修改经 Commit 提交一条可撤销命令，图 ID 登记进房间 ID 空间，撤销时释放。格坐标存于 BehaviorNode.Cell，扩建时由 Core 平移，这里不处理。
 * ObjectBehavior.Template(ownerId)：生成“房间解开 → 条件 → 隐藏自身”的默认图。
 * ObjectBehavior.CopyFor(graph, ownerId)：复制图并为图与节点分配新 ID，连线按新 ID 重连。
 * ObjectBehavior.Node / WithParameter / WithCanvas：构造节点、改单个参数、改画布坐标，节点不可变，返回新节点。
 * ObjectBehavior.GraphsOf(model, ownerId)、AllGraphs(model)：房间自有图加对象模板实例的图，模板图按 “实例 ID/图 ID” 改写 ID 并绑定实例为所有者。
 * ObjectBehavior.Issues(model)：校验图 ID 唯一、所有者存在、节点类型已登记、端口属于节点类型、setState 目标状态存在、同房显式目标存在。
 * ObjectBehavior.GraphTargetIssues 校验单图的对象、地形与恢复项引用。
 * ObjectBehavior.TargetIssues(model, lookup)：校验跨房 setVisible 目标，lookup 按房间 ID 取草稿，取不到时报错。
 * ObjectBehavior.Effects(graph, trigger)：从该触发器的入口节点出发按具名端口广度遍历，产出 setVisible、setState 与 Lua 合成的动作节点。
 * ObjectBehavior.IsVisual(graph)：只含触发器、无表达式条件、setVisible、setState 的图对规则无影响。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;

namespace Sokoban
{
    public sealed partial class RoomDraft
    {
        public string OwnerAt(Cell cell) => Room.ObjectAt(cell)?.Id ?? Room.TerrainAt(cell).EntityId;

        public BehaviorGraph GraphOf(Cell cell)
        {
            string owner = OwnerAt(cell);
            return owner == null ? null : ObjectBehavior.GraphsOf(Model, owner).FirstOrDefault();
        }

        public void SetBehavior(Cell cell, BehaviorGraph graph)
        {
            if (graph == null) throw new InvalidOperationException("行为图为空");
            var cells = Selection.Contains(cell) ? Selection.ToList() : new List<Cell> { cell };
            if (!cells.Contains(cell)) cells.Add(cell);
            foreach (var target in cells)
                if (Room.TerrainAt(target).CategoryId == TerrainCategories.Void) throw new InvalidOperationException("请选择地形或对象");
            var model = Model;
            var room = Room;
            var entities = new List<(Cell cell, TerrainCell before, AppearanceRef look, string id)>();
            var writes = new List<(BehaviorGraph before, BehaviorGraph after)>();
            foreach (var target in cells)
            {
                string owner = OwnerAt(target);
                if (owner == null)
                {
                    owner = "entity-" + Guid.NewGuid().ToString("N");
                    entities.Add((target, room.TerrainAt(target), room.AppearanceAt(target), owner));
                }
                else if (model.Instances.Any(i => i.Id == owner) && !model.Graphs.Any(g => g.OwnerId == owner))
                    throw new InvalidOperationException("对象模板实例的行为在模板中编辑");
                var previous = target == cell
                    ? model.Graphs.FirstOrDefault(g => g.Id == graph.Id)
                    : model.Graphs.FirstOrDefault(g => g.OwnerId == owner);
                if (previous != null && previous.OwnerId != owner) throw new InvalidOperationException("该行为属于其他对象");
                var value = target == cell
                    ? new BehaviorGraph(graph.Id ?? Guid.NewGuid().ToString("N"), owner, graph.Nodes, graph.Edges)
                    : ObjectBehavior.CopyFor(graph, owner, previous?.Id);
                writes.Add((previous, value));
            }
            Commit(new ModelEdit(() =>
            {
                foreach (var e in entities)
                {
                    room.SetTerrain(e.cell, e.before.CategoryId, e.id, e.before.State, e.before.Rotation);
                    room.SetAppearance(e.cell, e.look);
                }
                foreach (var w in writes)
                {
                    if (w.before == null) room.Ids.Add(w.after.Id, "graph");
                    int index = w.before == null ? -1 : model.Graphs.IndexOf(w.before);
                    if (index >= 0) model.Graphs[index] = w.after; else model.Graphs.Add(w.after);
                }
            }, () =>
            {
                for (int i = writes.Count - 1; i >= 0; i--)
                {
                    var w = writes[i];
                    int index = model.Graphs.IndexOf(w.after);
                    if (w.before == null)
                    {
                        if (index >= 0) model.Graphs.RemoveAt(index);
                        room.Ids.Remove(w.after.Id);
                    }
                    else if (index >= 0) model.Graphs[index] = w.before;
                }
                foreach (var e in entities)
                {
                    room.SetTerrain(e.cell, e.before.CategoryId, e.before.EntityId, e.before.State, e.before.Rotation);
                    room.SetAppearance(e.cell, e.look);
                }
            }));
            Select(cell);
            foreach (var target in cells) Selection.Add(target);
        }
    }

    public static class ObjectBehavior
    {
        public const string Condition = "condition";
        public const string SetVisible = "setVisible";
        public const string SetState = "setState";
        public const string Lua = "lua";
        public const string In = "in";
        public const string Out = "out";

        public static bool IsTrigger(string kind) => Registries.Triggers.Contains(kind);

        public static BehaviorNode Node(string kind, IDictionary<string, string> parameters = null, string id = null, Cell? cell = null) =>
            new BehaviorNode(id ?? Guid.NewGuid().ToString("N"), kind, cell,
                new Dictionary<string, string>(parameters ?? new Dictionary<string, string>(), StringComparer.Ordinal));

        public static BehaviorNode WithParameter(BehaviorNode node, string key, string value)
        {
            var parameters = new Dictionary<string, string>(node.Parameters.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal);
            if (value == null) parameters.Remove(key); else parameters[key] = value;
            return new BehaviorNode(node.Id, node.Kind, node.Cell, parameters);
        }

        public static BehaviorNode WithCanvas(BehaviorNode node, float x, float y) =>
            WithParameter(WithParameter(node, "canvasX", x.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture)),
                "canvasY", y.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));

        public static string Parameter(BehaviorNode node, string key) => node.Parameters.TryGetValue(key, out var value) ? value : null;

        public static BehaviorGraph Template(string ownerId = null)
        {
            var start = Node(TriggerIds.RoomSolved, null, "start");
            var when = Node(Condition, new Dictionary<string, string> { ["enabled"] = "true" }, "when");
            var hide = Node(SetVisible, new Dictionary<string, string> { ["visible"] = "false" }, "hide");
            var graph = new BehaviorGraph("template", ownerId, new[] { start, when, hide },
                new[] { new BehaviorEdge("start", Out, "when", In), new BehaviorEdge("when", "true", "hide", In) });
            return CopyFor(graph, ownerId);
        }

        public static BehaviorGraph CopyFor(BehaviorGraph source, string ownerId, string graphId = null)
        {
            var ids = source.Nodes.ToDictionary(n => n.Id, n => Guid.NewGuid().ToString("N"));
            var nodes = source.Nodes.Select(n => new BehaviorNode(ids[n.Id], n.Kind, n.Cell, n.Parameters)).ToList();
            var edges = source.Edges.Where(e => ids.ContainsKey(e.FromNode) && ids.ContainsKey(e.ToNode))
                .Select(e => new BehaviorEdge(ids[e.FromNode], e.FromPort, ids[e.ToNode], e.ToPort)).ToList();
            return new BehaviorGraph(graphId ?? Guid.NewGuid().ToString("N"), ownerId, nodes, edges);
        }

        public static IEnumerable<BehaviorGraph> GraphsOf(RoomDraftModel model, string ownerId)
        {
            foreach (var graph in model.Graphs)
                if (graph.OwnerId == ownerId) yield return graph;
            var instance = model.Instances.FirstOrDefault(i => i.Id == ownerId);
            if (instance == null || model.Graphs.Any(g => g.OwnerId == ownerId)) yield break;
            var template = ObjectTemplates.Find(model, instance.TemplateId);
            if (template == null) yield break;
            foreach (var graph in template.Graphs)
                yield return new BehaviorGraph(ownerId + "/" + graph.Id, ownerId, graph.Nodes, graph.Edges);
        }

        public static IEnumerable<BehaviorGraph> AllGraphs(RoomDraftModel model)
        {
            var owners = new HashSet<string>(model.Graphs.Select(g => g.OwnerId));
            foreach (var graph in model.Graphs) yield return graph;
            foreach (var instance in model.Instances)
                if (!owners.Contains(instance.Id))
                    foreach (var graph in GraphsOf(model, instance.Id)) yield return graph;
        }

        public static IEnumerable<string> Issues(RoomDraftModel model)
        {
            var graphIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var graph in model.Graphs)
            {
                if (string.IsNullOrWhiteSpace(graph.Id) || !graphIds.Add(graph.Id)) yield return "行为图 ID 无效或重复：" + graph.Id;
                if (graph.OwnerId == null || !model.Room.Ids.Contains(graph.OwnerId)) yield return $"行为图 {graph.Id} 找不到所有者 {graph.OwnerId}";
                foreach (var issue in GraphIssues(graph, StatesOf(model, graph.OwnerId))) yield return issue;
                foreach (var issue in GraphTargetIssues(model, graph, null, includeCrossRoom: false)) yield return issue;
            }
        }

        static ObjectStateTable StatesOf(RoomDraftModel model, string ownerId) => ownerId == null ? null : ObjectStateProfile.StatesOf(model, ownerId);

        public static IEnumerable<string> GraphIssues(BehaviorGraph graph, ObjectStateTable states)
        {
            var kinds = new Dictionary<string, BehaviorNodeKind>(StringComparer.Ordinal);
            foreach (var node in graph.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.Id) || kinds.ContainsKey(node.Id))
                {
                    yield return $"行为图 {graph.Id} 节点 ID 无效或重复：{node.Id}";
                    continue;
                }
                if (!Registries.BehaviorNodes.TryGet(node.Kind, out var kind))
                {
                    yield return $"行为图 {graph.Id} 节点 {node.Id} 的类型未知：{node.Kind}";
                    continue;
                }
                kinds.Add(node.Id, kind);
                if (node.Kind == SetState && (states == null || !states.States.Contains(Parameter(node, "state"))))
                    yield return $"行为图 {graph.Id} 节点 {node.Id} 引用了不存在的对象状态：{Parameter(node, "state")}";
            }
            foreach (var edge in graph.Edges)
            {
                if (!kinds.TryGetValue(edge.FromNode ?? "", out var from) || !kinds.TryGetValue(edge.ToNode ?? "", out var to))
                {
                    yield return $"行为图 {graph.Id} 存在悬空连线 {edge.FromNode} → {edge.ToNode}";
                    continue;
                }
                if (!from.Outputs.Contains(edge.FromPort)) yield return $"行为图 {graph.Id} 节点 {edge.FromNode} 没有输出端口 {edge.FromPort}";
                if (!to.Inputs.Contains(edge.ToPort)) yield return $"行为图 {graph.Id} 节点 {edge.ToNode} 没有输入端口 {edge.ToPort}";
            }
        }

        public static IEnumerable<string> TargetIssues(RoomDraftModel model, Func<string, RoomDraftModel> lookup) =>
            AllGraphs(model).SelectMany(graph => GraphTargetIssues(model, graph, lookup, false));

        public static IEnumerable<string> GraphTargetIssues(RoomDraftModel model, BehaviorGraph graph,
            Func<string, RoomDraftModel> lookup, bool includeLocal = true, bool includeCrossRoom = true)
        {
            foreach (var node in graph.Nodes)
            {
                string location = $"{model.Room.Id}/{graph.OwnerId}/{graph.Id}/{node.Id}";
                if (node.Kind == SetVisible)
                {
                    string id = Parameter(node, "targetObject"), level = Parameter(node, "targetRoom");
                    if (id == null) continue;
                    bool local = level == null || level == model.Room.Id;
                    if (local ? !includeLocal : !includeCrossRoom) continue;
                    var target = local ? model : lookup?.Invoke(level);
                    if (target == null || !target.Room.Ids.Contains(id))
                        yield return $"{location} 找不到目标 {(local ? id : level + "/" + id)}";
                }
                if (includeLocal && node.Kind == BehaviorNodeKinds.StartRecovery &&
                    !model.RecoveryItems.Any(item => item.Id == Parameter(node, "recovery")))
                    yield return $"{location} 找不到指定的恢复项";
                if (includeLocal && node.Kind == BehaviorNodeKinds.SetTerrainState)
                {
                    string terrain = Parameter(node, "targetTerrain");
                    if (terrain == null || !model.Room.Ids.Contains(terrain) || model.Room.Ids.KindOf(terrain) != "terrain")
                        yield return $"{location} 找不到指定的地形实体";
                }
            }
        }

        public static IEnumerable<BehaviorNode> Effects(BehaviorGraph graph, string trigger)
        {
            var queue = new Queue<BehaviorNode>(graph.Nodes.Where(n => n.Kind == trigger));
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                if (!seen.Add(node.Id)) continue;
                string port;
                if (IsTrigger(node.Kind)) port = Out;
                else switch (node.Kind)
                {
                    case Condition:
                        bool matched = Parameter(node, "enabled") != "false" && BehaviorLua.Evaluate(Parameter(node, "expression"), graph.OwnerId, true, null);
                        port = matched ? "true" : "false";
                        break;
                    case Lua:
                        var effects = new List<BehaviorNode>();
                        BehaviorLua.Evaluate(Parameter(node, "script"), graph.OwnerId, false, effects);
                        foreach (var effect in effects) yield return effect;
                        port = Out;
                        break;
                    case SetState:
                    case SetVisible:
                        yield return node;
                        port = Out;
                        break;
                    default: continue;
                }
                foreach (var edge in graph.Edges.Where(e => e.FromNode == node.Id && e.FromPort == port && e.ToPort == In))
                {
                    var next = graph.Nodes.FirstOrDefault(n => n.Id == edge.ToNode);
                    if (next != null) queue.Enqueue(next);
                }
            }
        }



        public static bool IsVisual(BehaviorGraph graph) => graph.Nodes.All(n =>
            IsTrigger(n.Kind) || n.Kind == SetVisible || n.Kind == SetState || n.Kind == Condition && Parameter(n, "expression") == null);
    }
}
