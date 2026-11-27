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
    public sealed class BehaviorGraphWindow : EditorWindow
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
        string selectedId;
        bool readOnly = true;
        bool deviceSelected;
        Button poseButton, templateButton;

        public static void Open()
        {
            var panel = WorldSceneSession.Panel;
            if (!WorldSceneSession.Active || panel == null || panel.Modal || !panel.Draft.SelectedCell.HasValue) return;
            var window = GetWindow<BehaviorGraphWindow>("行为图");
            window.minSize = new Vector2(520, 320);
            window.Show();
            window.Focus();
            window.Update();
        }

        public static void OpenRecoveryDevice()
        {
            var panel = WorldSceneSession.Panel;
            if (!WorldSceneSession.Active || panel == null || panel.Modal || panel.Draft.Model.RecoveryDevice == null) return;
            panel.Draft.Select(panel.Draft.Model.RecoveryDevice.Anchor);
            Open();
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
                const string multiline = "local a = 1\nlocal b = 2\nreturn a + b == 3";
                draft.Select(wall);
                window.Update();
                if (window.graph == null) throw new InvalidOperationException("重做后行为图窗口没有绑定图：panel=" + (window.panel != null) + " cell=" + window.cell + " readOnly=" + window.readOnly);
                if (!window.CanEdit()) throw new InvalidOperationException("重做后窗口不可编辑：revision=" + window.revision + " draft=" + draft.Revision + " readOnly=" + window.readOnly);
                window.Set(window.graph.Nodes[1].Id, "expression", multiline);
                window.Set(window.graph.Nodes[2].Id, "canvasW", "320");
                window.Set(window.graph.Nodes[2].Id, "canvasH", "180");
                var path = "Docs/验收证据/重建/阶段3/graphview-room.json";
                draft.Save(path, false);
                draft.Open(path, false);
                draft.Select(wall);
                window.Update();
                if (window.canvas.nodes.Count() != 4 || window.canvas.edges.Count() != 2 ||
                    ObjectBehavior.Parameter(window.graph.Nodes[1], "expression") != multiline ||
                    ObjectBehavior.Parameter(window.graph.Nodes[2], "visible") != "false" ||
                    ObjectBehavior.Parameter(window.graph.Nodes[0], "canvasX") != "150")
                    throw new InvalidOperationException("保存重开后图的结构、参数或位置变化");
                if (ObjectBehavior.Parameter(window.graph.Nodes[2], "canvasW") != "320" ||
                    ObjectBehavior.Parameter(window.graph.Nodes[2], "canvasH") != "180")
                    throw new InvalidOperationException("保存重开后节点宽高变化");
                var resized = window.nodes[window.graph.Nodes[2].Id].GetPosition();
                if (Mathf.Abs(resized.width - 320) > 1 || Mathf.Abs(resized.height - 180) > 1)
                    throw new InvalidOperationException("重开后画布中的节点尺寸没有还原");
                if (BehaviorLua.Check(multiline, false) != null) throw new InvalidOperationException("多行 Lua 脚本语法检查误报");
                if (BehaviorLua.Check("1 + 1 == 2", true) != null) throw new InvalidOperationException("条件表达式语法检查误报");
                if (BehaviorLua.Check(multiline, true) == null) throw new InvalidOperationException("多行语句用作条件表达式时应报错");
                if (BehaviorLua.Check("return (", true) == null) throw new InvalidOperationException("语法错误没有被检出");
                Debug.Log("STAGE3 PASS 创建、连线、共享撤销重做、保存重开、参数、节点位置与尺寸、多行 Lua 保持");
            }
            finally { DestroyImmediate(window); layout.Close(); }
        }

        public static void VerifyRecoveryDeviceAuthoring()
        {
            var layout = RoomLayoutWindow.Open();
            var draft = layout.Panel.Draft;
            BehaviorGraphWindow window = null;
            try
            {
                draft.Open("Docs/关卡样例/WS-01.json", true);
                if (draft.Model.RecoveryDevice == null) throw new InvalidOperationException("验证房间缺少恢复装置");
                OpenRecoveryDevice();
                window = GetWindow<BehaviorGraphWindow>();
                window.Update();
                if (!window.deviceSelected || !window.CanEdit() || window.graph.OwnerId != draft.Model.RecoveryDevice.Id)
                    throw new InvalidOperationException("恢复装置行为图未绑定");
                string id = window.graph.Id;
                var items = window.graph.Nodes.Concat(new[] { ObjectBehavior.Node(BehaviorNodeKinds.ShowText,
                    new Dictionary<string, string> { ["text"] = "verification" }) }).ToArray();
                if (!window.Commit(items, window.graph.Edges)) throw new InvalidOperationException("装置图未写入");
                if (!draft.RecoveryDeviceGraphs().Any(item => item.Id == id && item.Nodes.Any(node => ObjectBehavior.Parameter(node, "text") == "verification")))
                    throw new InvalidOperationException("装置图内容错误");
                window.OpenPose();
                LuaEditorWindow.VerifyRecoveryPoseEditor(draft);
                const string path = "Docs/验收证据/重建/阶段3/device-authoring-room.json";
                draft.Save(path, false);
                draft.Open(path, false);
                OpenRecoveryDevice();
                window.Update();
                if (!window.deviceSelected || !draft.RecoveryDeviceGraphs().Any(item => item.Id == id && item.Nodes.Any(node => ObjectBehavior.Parameter(node, "text") == "verification")))
                    throw new InvalidOperationException("重新打开后装置图未恢复");
                if (!draft.GetRecoveryPoseSource().Contains("function pose(t, ctx) return {{id=ctx.boxId, x=1, y=0, z=2}} end"))
                    throw new InvalidOperationException("重新打开后姿态脚本未恢复");
                Debug.Log("DEVICE AUTHORING PASS 装置图、姿态校验、撤销重做与保存重开");
            }
            finally
            {
                if (window) window.Close();
                layout.Close();
            }
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
            templateButton = new ToolbarButton(UseDeviceTemplate) { text = "装置默认图" };
            poseButton = new ToolbarButton(OpenPose) { text = "编辑恢复姿态 Lua" };
            bar.Add(templateButton);
            bar.Add(poseButton);
            rootVisualElement.Add(bar);
            canvas = new Canvas { style = { flexGrow = 1 } };
            var grid = new GridBackground();
            canvas.Insert(0, grid);
            grid.StretchToParentSize();
            canvas.SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            canvas.AddManipulator(new ContentDragger());
            canvas.AddManipulator(new SelectionDragger());
            canvas.AddManipulator(new RectangleSelector());
            var map = new MiniMap { anchored = false };
            map.style.position = Position.Absolute;
            map.style.right = 10;
            map.style.bottom = 10;
            map.style.left = StyleKeyword.Auto;
            map.style.top = StyleKeyword.Auto;
            map.style.width = 160;
            map.style.height = 100;
            map.style.minWidth = 160;
            map.style.maxWidth = 160;
            map.style.minHeight = 100;
            map.style.maxHeight = 100;
            canvas.Add(map);
            bar.Add(new ToolbarToggle { text = "小地图", value = true });
            ((ToolbarToggle)bar.Children().Last()).RegisterValueChangedCallback(evt => map.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None);
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
            canvas.RegisterCallback<MouseUpEvent>(_ => CommitSizes(), TrickleDown.NoTrickleDown);
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
                && selectionCount == (draft?.Selection.Count ?? 0) && selectedId == draft?.Selected) return;
            panel = next;
            model = draft?.Model;
            cell = draft?.SelectedCell;
            revision = draft?.Revision ?? -1;
            selectionCount = draft?.Selection.Count ?? 0;
            selectedId = draft?.Selected;
            deviceSelected = draft != null && draft.Model.RecoveryDevice != null && draft.Selected == draft.Model.RecoveryDevice.Id;
            bool selected = deviceSelected || cell.HasValue && draft.Room.TerrainAt(cell.Value).CategoryId != TerrainCategories.Void;
            string owner = deviceSelected ? draft.Model.RecoveryDevice.Id : selected ? draft.OwnerAt(cell.Value) : null;
            graph = deviceSelected ? draft.RecoveryDeviceGraphs().FirstOrDefault() : selected ? draft.GraphOf(cell.Value) : null;
            readOnly = !selected || owner != null && model.Instances.Any(item => item.Id == owner) && !model.Graphs.Any(item => item.OwnerId == owner);
            if (selected && graph == null)
                graph = deviceSelected ? draft.RecoveryDeviceGraphTemplate() : new BehaviorGraph(Guid.NewGuid().ToString("N"), owner, Array.Empty<BehaviorNode>(), Array.Empty<BehaviorEdge>());
            templateButton.SetEnabled(deviceSelected && !readOnly);
            poseButton.SetEnabled(deviceSelected && !readOnly);
            title.text = selected ? $"{draft.ActiveId}  ·  {owner ?? cell.Value.ToString()}" + BatchLabel() : "在 Scene View 选择地形或物件";
            status.text = selected && readOnly ? "此物件继承模板行为。" : "";
            Rebuild();
        }

        string BatchLabel() => !deviceSelected && selectionCount > 1 ? $"  ·  {selectionCount} 格，分别保存独立副本" : "";

        void UseDeviceTemplate()
        {
            if (!CanEdit() || !deviceSelected) return;
            if (panel.Draft.RecoveryDeviceGraphs().Any(item => item.Id == graph.Id) &&
                !EditorUtility.DisplayDialog("替换装置行为图", "载入默认图会替换当前图的节点和连线。", "替换", "取消")) return;
            var template = panel.Draft.RecoveryDeviceGraphTemplate();
            if (Commit(template.Nodes, template.Edges)) Rebuild();
        }

        void OpenPose()
        {
            if (!CanEdit() || !deviceSelected) return;
            var draft = panel.Draft;
            var boundModel = draft.Model;
            string roomId = draft.ActiveId, deviceId = boundModel.RecoveryDevice.Id;
            bool Bound() => WorldSceneSession.Active && WorldSceneSession.Panel == panel && panel.Draft == draft
                && draft.Model == boundModel && draft.ActiveId == roomId && draft.Model.RecoveryDevice?.Id == deviceId;
            try
            {
                LuaEditorWindow.OpenRecoveryPose(roomId + " · " + deviceId + " · 恢复姿态", draft.GetRecoveryPoseSource(),
                    () => Bound() ? draft.Revision + "/" + draft.Model.RecoveryDevice.ScriptRef : null,
                    () => Bound() ? draft.GetRecoveryPoseSource() : null,
                    () => Bound() ? draft.GetDefaultRecoveryPoseSource() : null,
                    text => { if (!Bound()) throw new InvalidOperationException("装置或房间已切换，请重新打开编辑器"); draft.SetRecoveryPoseSource(text); });
            }
            catch (Exception error) { status.text = error.Message; }
        }

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
            if (!readOnly) node.capabilities |= Capabilities.Resizable;
            node.SetPosition(new Rect(Coordinate(item, "canvasX", 180 + index % 3 * 270), Coordinate(item, "canvasY", 45 + index / 3 * 165),
                Mathf.Max(180, Coordinate(item, "canvasW", 230)), Mathf.Max(100, Coordinate(item, "canvasH", 100))));
            if (!readOnly) node.Add(new ResizableElement());
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
                case BehaviorNodeKinds.StartRecovery:
                    node.extensionContainer.Add(new Label("启动本房恢复装置"));
                    break;
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
            field.style.flexDirection = FlexDirection.Column;
            field.labelElement.style.minWidth = 0;
            field.labelElement.style.whiteSpace = WhiteSpace.Normal;
            var input = field.Q(TextField.textInputUssName);
            input.style.flexGrow = 1;
            input.style.minWidth = 120;
            if (multiline) field.style.minHeight = 52;
            field.SetEnabled(!readOnly);
            field.RegisterValueChangedCallback(evt => Set(item.Id, key, string.IsNullOrEmpty(evt.newValue) ? null : evt.newValue));
            node.extensionContainer.Add(field);
            if (key == "expression" || key == "script")
            {
                string heading = graph.OwnerId + " · " + label;
                bool isCondition = key == "expression";
                var expand = new Button(() => LuaEditorWindow.Open(heading, field.value, isCondition, text => field.value = text)) { text = "展开编辑" };
                expand.SetEnabled(!readOnly);
                node.extensionContainer.Add(expand);
            }
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

        void CommitSizes()
        {
            if (!CanEdit()) return;
            bool changed = false;
            var updated = graph.Nodes.Select(item =>
            {
                if (!nodes.TryGetValue(item.Id, out var node)) return item;
                var rect = node.GetPosition();
                if (Mathf.Abs(rect.width - Mathf.Max(180, Coordinate(item, "canvasW", 230))) < 1 &&
                    Mathf.Abs(rect.height - Mathf.Max(100, Coordinate(item, "canvasH", 100))) < 1) return item;
                changed = true;
                var sized = ObjectBehavior.WithParameter(item, "canvasW", Mathf.Round(rect.width).ToString(CultureInfo.InvariantCulture));
                return ObjectBehavior.WithParameter(sized, "canvasH", Mathf.Round(rect.height).ToString(CultureInfo.InvariantCulture));
            }).ToList();
            if (changed) Commit(updated, graph.Edges);
        }

        bool CanEdit() => !readOnly && graph != null && panel != null && !panel.Modal && model == panel.Draft.Model
            && cell == panel.Draft.SelectedCell && revision == panel.Draft.Revision
            && (!deviceSelected || panel.Draft.Selected == model.RecoveryDevice?.Id);

        bool Commit(IReadOnlyList<BehaviorNode> items, IReadOnlyList<BehaviorEdge> edges)
        {
            var draft = panel.Draft;
            string id = graph.Id;
            panel.Run(() =>
            {
                var value = new BehaviorGraph(id, graph.OwnerId, items, edges);
                if (deviceSelected) draft.SetRecoveryDeviceGraph(value); else draft.SetBehavior(cell.Value, value);
            }, "已更新行为图");
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
