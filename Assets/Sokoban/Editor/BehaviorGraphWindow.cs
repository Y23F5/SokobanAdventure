/**
 * @file BehaviorGraphWindow.cs
 * @brief Open 打开独立窗口；Update 跟随草稿选择与共享历史；Rebuild 还原节点、端口、位置与连线；Commit 通过 RoomDraft 保存编辑。
 * VerifyRoundTrip 通过实际窗口操作验证构图、共享撤销和房间文件往返。
 * Change 处理画布增删和移动；ShowIssues 列出图与目标错误，支持定位；Copy 与 Paste 复制所选子图并替换节点 ID。
 */
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Sokoban.Core;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sokoban.Editor
{
    internal sealed class BehaviorGraphWindow : EditorWindow
    {
        static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { TriggerIds.RoomSolved, "事件/房间解开" }, { TriggerIds.FirstPush, "事件/首次推动" },
            { TriggerIds.RecoveryStart, "事件/恢复开始" }, { TriggerIds.RecoveryEnd, "事件/恢复完成" },
            { TriggerIds.OnInteract, "事件/按 F 交互" }, { BehaviorNodeKinds.Condition, "条件/开关与 Lua" },
            { BehaviorNodeKinds.SetVisible, "动作/显示或隐藏对象" }, { BehaviorNodeKinds.SetState, "动作/切换状态" },
            { BehaviorNodeKinds.StartRecovery, "动作/启动恢复" }, { BehaviorNodeKinds.SetTerrainState, "动作/设置地形开关" },
            { BehaviorNodeKinds.ShowText, "动作/显示文字" }, { BehaviorNodeKinds.PlaySound, "动作/播放音效" },
            { BehaviorNodeKinds.Lua, "脚本/Lua" }
        };

        sealed class Canvas : GraphView
        {
            internal Action<DropdownMenu, Vector2> AddMenu;

            public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
            {
                base.BuildContextualMenu(evt);
                AddMenu?.Invoke(evt.menu, contentViewContainer.WorldToLocal(evt.mousePosition));
            }

            public override List<Port> GetCompatiblePorts(Port start, NodeAdapter adapter) =>
                ports.ToList().Where(port => port.node != start.node && port.direction != start.direction).ToList();
        }

        const string ClipboardKey = "Sokoban.BehaviorGraph";
        static BehaviorGraph clipboard;
        readonly Dictionary<string, Node> nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
        Canvas canvas;
        Label title, status;
        ScrollView issues;
        LayoutPanel panel;
        RoomDraftModel model;
        Cell? cell;
        BehaviorGraph graph;
        int revision = -1, selectionCount;
        bool readOnly = true;

        internal static void Open()
        {
            var panel = WorldSceneSession.Panel;
            if (!WorldSceneSession.Active || panel == null || panel.Modal || !panel.Draft.SelectedCell.HasValue) return;
            var window = GetWindow<BehaviorGraphWindow>("行为图");
            window.minSize = new Vector2(520, 320);
            window.Show();
            window.Focus();
            window.Update();
        }

        public static void VerifyRoundTrip()
        {
            var layout = RoomLayoutWindow.Open();
            var draft = layout.Panel.Draft;
            draft.Open("Docs/关卡样例/WS-01.json", true);
            var wall = new Cell(2, 0);
            draft.Select(wall);
            var window = CreateInstance<BehaviorGraphWindow>();
            try
            {
                window.CreateGUI();
                window.Add(TriggerIds.RoomSolved, new Vector2(150, 40));
                window.Add(BehaviorNodeKinds.Condition, new Vector2(390, 40));
                window.Add(BehaviorNodeKinds.SetVisible, new Vector2(630, 40));
                window.Add(TriggerIds.OnInteract, new Vector2(150, 200));
                window.Set(window.graph.Nodes[1].Id, "expression", "true");
                window.Set(window.graph.Nodes[2].Id, "visible", "false");
                var newEdges = new List<Edge>();
                for (int i = 0; i < 2; i++)
                {
                    var output = window.nodes[window.graph.Nodes[i].Id].outputContainer.Children().OfType<Port>().First();
                    var input = window.nodes[window.graph.Nodes[i + 1].Id].inputContainer.Children().OfType<Port>().Single();
                    newEdges.Add(output.ConnectTo(input));
                }
                var change = window.Change(new GraphViewChange { edgesToCreate = newEdges });
                foreach (var edge in change.edgesToCreate) window.canvas.AddElement(edge);
                var graph = draft.GraphOf(wall);
                if (graph.Nodes.Count != 4 || graph.Edges.Count != 2 || window.canvas.edges.Count() != 2)
                    throw new InvalidOperationException("节点或连线缺失");
                window.History(false);
                if (draft.GraphOf(wall).Edges.Count != 0) throw new InvalidOperationException("撤销后连线仍存在");
                window.History(true);
                if (draft.GraphOf(wall).Edges.Count != 2) throw new InvalidOperationException("重做未恢复两条连线");
                var path = "Docs/验收证据/重建/阶段3/graphview-room.json";
                draft.Save(path, false);
                draft.Open(path, false);
                draft.Select(wall);
                window.Update();
                if (window.canvas.nodes.Count() != 4 || window.canvas.edges.Count() != 2 ||
                    ObjectBehavior.Parameter(window.graph.Nodes[1], "expression") != "true" ||
                    ObjectBehavior.Parameter(window.graph.Nodes[2], "visible") != "false" ||
                    ObjectBehavior.Parameter(window.graph.Nodes[0], "canvasX") != "150")
                    throw new InvalidOperationException("保存重开后图的结构、参数或位置变化");
                Debug.Log("STAGE3 PASS 创建、连线、共享撤销重做、保存重开、参数与节点位置保持");
            }
            finally { DestroyImmediate(window); layout.Close(); }
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var bar = new Toolbar();
            title = new Label { style = { flexGrow = 1 } };
            bar.Add(title);
            var add = new ToolbarMenu { text = "新建节点" };
            FillMenu(add.menu, () => canvas.contentViewContainer.WorldToLocal(canvas.worldBound.center));
            bar.Add(add);
            bar.Add(new ToolbarButton(() => History(false)) { text = "撤销" });
            bar.Add(new ToolbarButton(() => History(true)) { text = "重做" });
            bar.Add(new ToolbarButton(() => canvas.FrameAll()) { text = "显示全部" });
            rootVisualElement.Add(bar);
            canvas = new Canvas { style = { flexGrow = 1 } };
            var grid = new GridBackground();
            canvas.Insert(0, grid);
            grid.StretchToParentSize();
            canvas.SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            canvas.AddManipulator(new ContentDragger());
            canvas.AddManipulator(new SelectionDragger());
            canvas.AddManipulator(new RectangleSelector());
            var map = new MiniMap { anchored = true };
            map.SetPosition(new Rect(10, 10, 150, 94));
            canvas.Add(map);
            canvas.AddMenu = (menu, position) => FillMenu(menu, () => position);
            canvas.serializeGraphElements = Copy;
            canvas.canPasteSerializedData = text => CanEdit() && text == ClipboardKey && clipboard != null;
            canvas.unserializeAndPaste = (_, text) => Paste(text);
            canvas.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (!(evt.ctrlKey || evt.commandKey) || (evt.keyCode != KeyCode.Z && evt.keyCode != KeyCode.Y)) return;
                if (evt.target is VisualElement element && element.GetFirstAncestorOfType<TextField>() != null) return;
                History(evt.keyCode == KeyCode.Y || evt.shiftKey);
                evt.StopPropagation();
                evt.PreventDefault();
            }, TrickleDown.TrickleDown);
            rootVisualElement.Add(canvas);
            issues = new ScrollView { style = { maxHeight = 100 } };
            status = new Label { style = { whiteSpace = WhiteSpace.Normal } };
            rootVisualElement.Add(issues);
            rootVisualElement.Add(status);
            title.text = "在 Scene View 选择地形或物件";
            model = null;
            Update();
        }

        void Update()
        {
            if (canvas == null) return;
            var next = WorldSceneSession.Active ? WorldSceneSession.Panel : null;
            var draft = next?.Draft;
            if (panel == next && model == draft?.Model && cell == draft?.SelectedCell && revision == (draft?.Revision ?? -1)
                && selectionCount == (draft?.Selection.Count ?? 0)) return;
            panel = next;
            model = draft?.Model;
            cell = draft?.SelectedCell;
            revision = draft?.Revision ?? -1;
            selectionCount = draft?.Selection.Count ?? 0;
            bool selected = cell.HasValue && draft.Room.TerrainAt(cell.Value).CategoryId != TerrainCategories.Void;
            string owner = selected ? draft.OwnerAt(cell.Value) : null;
            graph = selected ? draft.GraphOf(cell.Value) : null;
            readOnly = !selected || owner != null && model.Instances.Any(item => item.Id == owner) && !model.Graphs.Any(item => item.OwnerId == owner);
            if (selected && graph == null)
                graph = new BehaviorGraph(Guid.NewGuid().ToString("N"), owner, Array.Empty<BehaviorNode>(), Array.Empty<BehaviorEdge>());
            title.text = selected ? $"{draft.ActiveId}  ·  {owner ?? cell.Value.ToString()}" + BatchLabel() : "在 Scene View 选择地形或物件";
            status.text = selected && readOnly ? "此物件继承模板行为。" : "";
            Rebuild();
        }

        string BatchLabel() => selectionCount > 1 ? $"  ·  {selectionCount} 格，分别保存独立副本" : "";

        void Rebuild()
        {
            canvas.graphViewChanged = null;
            canvas.DeleteElements(canvas.edges.Cast<GraphElement>().Concat(canvas.nodes).ToArray());
            nodes.Clear();
            if (graph != null)
            {
                for (int i = 0; i < graph.Nodes.Count; i++) DrawNode(graph.Nodes[i], i);
                foreach (var edge in graph.Edges) DrawEdge(edge);
            }
            canvas.graphViewChanged = Change;
            ShowIssues();
        }

        void FillMenu(DropdownMenu menu, Func<Vector2> position)
        {
            foreach (var entry in Labels)
                menu.AppendAction(entry.Value, _ => Add(entry.Key, position()), _ => CanEdit() ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        void Add(string kind, Vector2 position)
        {
            if (!CanEdit()) return;
            var item = ObjectBehavior.WithCanvas(ObjectBehavior.Node(kind), position.x, position.y);
            if (kind == BehaviorNodeKinds.StartRecovery) item = ObjectBehavior.WithParameter(item, "recovery", model.RecoveryItems.FirstOrDefault()?.Id);
            if (kind == BehaviorNodeKinds.SetTerrainState) item = ObjectBehavior.WithParameter(item, "state", "on");
            if (Commit(graph.Nodes.Concat(new[] { item }).ToList(), graph.Edges)) DrawNode(item, graph.Nodes.Count - 1);
        }

        void DrawNode(BehaviorNode item, int index)
        {
            if (string.IsNullOrEmpty(item.Id) || nodes.ContainsKey(item.Id)) return;
            var node = new Node { title = Labels.TryGetValue(item.Kind, out var label) ? label.Split('/').Last() : item.Kind, userData = item.Id };
            if (readOnly) node.capabilities &= ~(Capabilities.Deletable | Capabilities.Movable);
            if (Registries.BehaviorNodes.TryGet(item.Kind, out var definition))
            {
                foreach (string id in definition.Inputs) PortFor(node, id, Direction.Input);
                foreach (string id in definition.Outputs) PortFor(node, id, Direction.Output);
            }
            node.SetPosition(new Rect(Coordinate(item, "canvasX", 180 + index % 3 * 270), Coordinate(item, "canvasY", 45 + index / 3 * 165), 230, 100));
            Parameters(node, item);
            node.RefreshExpandedState();
            node.RefreshPorts();
            nodes.Add(item.Id, node);
            canvas.AddElement(node);
        }

        static float Coordinate(BehaviorNode item, string key, float fallback) =>
            float.TryParse(ObjectBehavior.Parameter(item, key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && !float.IsNaN(value) && !float.IsInfinity(value) ? value : fallback;

        void PortFor(Node node, string id, Direction direction)
        {
            var port = node.InstantiatePort(Orientation.Horizontal, direction, Port.Capacity.Multi, typeof(bool));
            port.portName = id;
            port.userData = id;
            port.SetEnabled(!readOnly);
            (direction == Direction.Input ? node.inputContainer : node.outputContainer).Add(port);
        }

        void DrawEdge(BehaviorEdge item)
        {
            if (!nodes.TryGetValue(item.FromNode ?? "", out var source) || !nodes.TryGetValue(item.ToNode ?? "", out var target)) return;
            var output = source.outputContainer.Children().OfType<Port>().FirstOrDefault(port => (string)port.userData == item.FromPort);
            var input = target.inputContainer.Children().OfType<Port>().FirstOrDefault(port => (string)port.userData == item.ToPort);
            if (output == null || input == null) return;
            var edge = output.ConnectTo(input);
            edge.userData = item;
            if (readOnly) edge.capabilities &= ~Capabilities.Deletable;
            canvas.AddElement(edge);
        }

        void Parameters(Node node, BehaviorNode item)
        {
            switch (item.Kind)
            {
                case BehaviorNodeKinds.Condition: Flag(node, item, "条件启用", "enabled", true); Field(node, item, "Lua 表达式", "expression"); break;
                case BehaviorNodeKinds.SetVisible:
                    Flag(node, item, "显示对象", "visible", false);
                    Field(node, item, "目标房间 ID", "targetRoom");
                    Field(node, item, "目标对象 ID（默认自身）", "targetObject");
                    break;
                case BehaviorNodeKinds.SetState:
                    var states = graph.OwnerId == null ? null : ObjectStateProfile.StatesOf(model, graph.OwnerId)?.States;
                    Choice(node, item, "对象状态", "state", states ?? Array.Empty<string>());
                    break;
                case BehaviorNodeKinds.StartRecovery: Choice(node, item, "恢复项", "recovery", model.RecoveryItems.Select(r => r.Id)); break;
                case BehaviorNodeKinds.SetTerrainState:
                    Field(node, item, "地形实体 ID", "targetTerrain");
                    Choice(node, item, "地形开关", "state", new[] { "on", "off" });
                    break;
                case BehaviorNodeKinds.ShowText: Field(node, item, "文字", "text", true); break;
                case BehaviorNodeKinds.PlaySound: Choice(node, item, "音效", "sound", PuzzleHost.BuiltinSounds); break;
                case BehaviorNodeKinds.Lua: Field(node, item, "Lua 脚本", "script", true); break;
            }
        }

        void Field(Node node, BehaviorNode item, string label, string key, bool multiline = false)
        {
            var field = new TextField(label) { value = ObjectBehavior.Parameter(item, key) ?? "", isDelayed = true, multiline = multiline };
            if (multiline) field.style.minHeight = 52;
            field.SetEnabled(!readOnly);
            field.RegisterValueChangedCallback(evt => Set(item.Id, key, string.IsNullOrEmpty(evt.newValue) ? null : evt.newValue));
            node.extensionContainer.Add(field);
        }

        void Choice(Node node, BehaviorNode item, string label, string key, IEnumerable<string> choices)
        {
            var values = choices.ToList();
            string current = ObjectBehavior.Parameter(item, key) ?? "";
            if (!values.Contains(current)) values.Insert(0, current);
            var field = new PopupField<string>(label, values, values.IndexOf(current));
            field.SetEnabled(!readOnly);
            field.RegisterValueChangedCallback(evt => Set(item.Id, key, string.IsNullOrEmpty(evt.newValue) ? null : evt.newValue));
            node.extensionContainer.Add(field);
        }

        void Flag(Node node, BehaviorNode item, string label, string key, bool fallback)
        {
            var field = new Toggle(label) { value = ObjectBehavior.Parameter(item, key) is string value ? value != "false" : fallback };
            field.SetEnabled(!readOnly);
            field.RegisterValueChangedCallback(evt => Set(item.Id, key, evt.newValue ? "true" : "false"));
            node.extensionContainer.Add(field);
        }

        void Set(string id, string key, string value)
        {
            if (CanEdit()) Commit(graph.Nodes.Select(item => item.Id == id ? ObjectBehavior.WithParameter(item, key, value) : item).ToList(), graph.Edges);
        }

        GraphViewChange Change(GraphViewChange change)
        {
            if (!CanEdit()) return default;
            var removals = change.elementsToRemove ?? new List<GraphElement>();
            var removedNodes = new HashSet<string>(removals.OfType<Node>().Select(node => (string)node.userData));
            var removedEdges = new HashSet<BehaviorEdge>(removals.OfType<Edge>().Select(edge => edge.userData as BehaviorEdge));
            var moved = (change.movedElements ?? new List<GraphElement>()).OfType<Node>().ToDictionary(node => (string)node.userData);
            var created = change.edgesToCreate ?? new List<Edge>();
            if (removedNodes.Count + removedEdges.Count + moved.Count + created.Count == 0) return change;
            var updated = graph.Nodes.Where(item => !removedNodes.Contains(item.Id)).Select(item => moved.TryGetValue(item.Id, out var node)
                ? ObjectBehavior.WithCanvas(item, node.GetPosition().x, node.GetPosition().y) : item).ToList();
            var edges = graph.Edges.Where(item => !removedEdges.Contains(item) && !removedNodes.Contains(item.FromNode) && !removedNodes.Contains(item.ToNode)).ToList();
            foreach (var edge in created)
            {
                var item = new BehaviorEdge((string)edge.output.node.userData, (string)edge.output.userData,
                    (string)edge.input.node.userData, (string)edge.input.userData);
                edge.userData = item;
                edges.Add(item);
            }
            if (!Commit(updated, edges)) return default;
            foreach (var id in removedNodes) nodes.Remove(id);
            return change;
        }

        bool CanEdit() => !readOnly && graph != null && panel != null && !panel.Modal && model == panel.Draft.Model
            && cell == panel.Draft.SelectedCell && revision == panel.Draft.Revision;

        bool Commit(IReadOnlyList<BehaviorNode> items, IReadOnlyList<BehaviorEdge> edges)
        {
            var draft = panel.Draft;
            string id = graph.Id;
            panel.Run(() => draft.SetBehavior(cell.Value, new BehaviorGraph(id, graph.OwnerId, items, edges)), "已更新行为图");
            status.text = panel.Status;
            if (draft.Revision == revision) return false;
            revision = draft.Revision;
            graph = model.Graphs.First(item => item.Id == id);
            title.text = $"{draft.ActiveId}  ·  {graph.OwnerId}" + BatchLabel();
            ShowIssues();
            return true;
        }

        void History(bool redo)
        {
            if (panel == null || panel.Modal) return;
            if (redo) panel.Run(panel.Draft.Redo, "已重做"); else panel.Run(panel.Draft.Undo, "已撤销");
            SceneView.RepaintAll();
            Update();
        }

        void ShowIssues()
        {
            issues.Clear();
            if (graph == null) return;
            var states = graph.OwnerId == null ? null : ObjectStateProfile.StatesOf(model, graph.OwnerId);
            var messages = ObjectBehavior.GraphIssues(graph, states).Concat(ObjectBehavior.GraphTargetIssues(model, graph,
                id => panel.Draft.Catalog.TryGetValue(id, out var room) ? room : null));
            foreach (string message in messages)
                issues.Add(new Button(() => Locate(message)) { text = message });
        }

        void Locate(string message)
        {
            var node = nodes.FirstOrDefault(pair => message.Contains("/" + pair.Key + " ") || message.Contains(" " + pair.Key + " ")
                || message.EndsWith(" " + pair.Key, StringComparison.Ordinal)).Value;
            if (node == null) return;
            canvas.ClearSelection();
            canvas.AddToSelection(node);
            canvas.FrameSelection();
        }

        string Copy(IEnumerable<GraphElement> selection)
        {
            var ids = new HashSet<string>(selection.OfType<Node>().Select(item => (string)item.userData));
            if (graph == null || ids.Count == 0) return "";
            var copied = graph.Nodes.Where(item => ids.Contains(item.Id)).Select(item =>
            {
                var position = nodes[item.Id].GetPosition();
                return ObjectBehavior.WithCanvas(item, position.x, position.y);
            }).ToList();
            clipboard = new BehaviorGraph(null, null, copied, graph.Edges.Where(item => ids.Contains(item.FromNode) && ids.Contains(item.ToNode)).ToList());
            return ClipboardKey;
        }

        void Paste(string text)
        {
            if (text != ClipboardKey || clipboard == null || !CanEdit()) return;
            var copy = ObjectBehavior.CopyFor(clipboard, graph.OwnerId);
            var pasted = copy.Nodes.Select(item => ObjectBehavior.WithCanvas(item, Coordinate(item, "canvasX", 0) + 32, Coordinate(item, "canvasY", 0) + 32));
            if (Commit(graph.Nodes.Concat(pasted).ToList(), graph.Edges.Concat(copy.Edges).ToList())) Rebuild();
        }
    }
}
