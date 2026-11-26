/**
 * @file Validation.cs
 * @brief 两档校验共用同一份判定：每条规则只写一次，同时声明草稿级与发布级的严重级别，档位只决定严重级别的映射。
 * Severity：提示、警告、错误三档严重级别。
 * ValidationStage：草稿级与发布级两个档位。
 * Issue：一条校验结果，带规则 ID、定位（格子或对象 ID）与原因文字。
 * Rule：针对房间与入口缓冲格的规则，声明规则 ID、草稿级与发布级严重级别以及判定函数。
 * DraftRule：针对整份草稿的规则，声明规则 ID、两档严重级别以及判定函数。
 * RoomValidator.Validate(room, stage, entry)：运行全部 Rule 并按档位映射严重级别，返回结果列表；object.notOnFloor 与 object.sharedCell 同样覆盖自定义物件；entry 为主入口内侧的入口缓冲格，缺主入口时传 null。
 * RoomValidator.HasErrors(issues)：判断结果中是否存在错误，发布级存在错误时阻止试玩、保存与导出。
 * RoomValidator.ValidateDraft(draft, stage)：在 Validate 之上运行 DraftRules：recovery.none、door.geometry、boundary.open，
 * 以及 recovery.duration（时长不大于 0）、id.duplicate（各类 ID 重复）、ref.dangling（模板、行为图所有者与连线节点、对象状态键、界面图资源、主入口的悬空引用）、
 * recovery.scriptUnknown（脚本引用既不是随项目发布的默认脚本也不是房间内 script 类资源）、behavior.nodeUnknown（行为节点类型不在注册表，含模板内的图）、
 * openable.disconnected（存在可开合地形时，以封闭状态从出生点出发走不到某扇门，两档均为警告）。
 * RoomValidator.ModelFootprintRuleId 与 ModelFootprintIssue(assetId, sizeX, sizeZ)：单格模型水平包围盒必须为 1×1 格、容差 0.001，两档均为错误；包围盒由表现层测得后传入，Core 只判定。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public enum Severity { Hint, Warning, Error }

    public enum ValidationStage { Draft, Publish }

    public sealed class Issue
    {
        public string RuleId { get; }
        public Severity Severity { get; }
        public string Location { get; }
        public string Message { get; }

        public Issue(string ruleId, Severity severity, string location, string message)
        {
            RuleId = ruleId;
            Severity = severity;
            Location = location;
            Message = message;
        }
    }

    public sealed class Rule
    {
        public string Id { get; }
        public Severity Draft { get; }
        public Severity Publish { get; }
        public Func<Room, Cell?, IEnumerable<(string location, string message)>> Check { get; }

        public Rule(string id, Severity draft, Severity publish,
            Func<Room, Cell?, IEnumerable<(string location, string message)>> check)
        {
            Id = id;
            Draft = draft;
            Publish = publish;
            Check = check;
        }
    }

    public sealed class DraftRule
    {
        public string Id { get; }
        public Severity Draft { get; }
        public Severity Publish { get; }
        public Func<RoomDraftModel, IEnumerable<(string location, string message)>> Check { get; }

        public DraftRule(string id, Severity draft, Severity publish,
            Func<RoomDraftModel, IEnumerable<(string location, string message)>> check)
        {
            Id = id;
            Draft = draft;
            Publish = publish;
            Check = check;
        }
    }

    public static class RoomValidator
    {
        public const string ModelFootprintRuleId = "model.footprint";
        public const float FootprintTolerance = 0.001f;

        public static readonly IReadOnlyList<Rule> Rules = new List<Rule>
        {
            new Rule("spawn.missing", Severity.Hint, Severity.Error, (room, entry) =>
                room.ObjectsOf(ObjectCategories.Spawn).Any()
                    ? Enumerable.Empty<(string, string)>()
                    : new[] { (room.Id, "缺少出生点") }),

            new Rule("spawn.multiple", Severity.Error, Severity.Error, (room, entry) =>
                room.ObjectsOf(ObjectCategories.Spawn).Skip(1).Select(o => (o.Id, "出生点多于一个"))),

            new Rule("object.notOnFloor", Severity.Error, Severity.Error, (room, entry) =>
                room.Objects.Where(o => room.TerrainAt(o.Cell).CategoryId != TerrainCategories.Floor)
                    .Select(o => (o.Id, "对象不在地板格上"))),

            new Rule("object.sharedCell", Severity.Error, Severity.Error, (room, entry) =>
                room.Objects.GroupBy(o => o.Cell).Where(g => g.Count() > 1)
                    .SelectMany(g => g.Skip(1).Select(o => (o.Id, "同一格有多个对象")))),

            new Rule("entry.occupied", Severity.Error, Severity.Error, (room, entry) =>
                entry.HasValue && room.ObjectAt(entry.Value) != null && room.ObjectAt(entry.Value).CategoryId != ObjectCategories.Spawn
                    ? new[] { (room.ObjectAt(entry.Value).Id, "入口缓冲格被对象占用") }
                    : Enumerable.Empty<(string, string)>()),

            new Rule("target.none", Severity.Hint, Severity.Error, (room, entry) =>
                room.ObjectsOf(ObjectCategories.Target).Any()
                    ? Enumerable.Empty<(string, string)>()
                    : new[] { (room.Id, "没有目标") }),

            new Rule("target.exceedsBoxes", Severity.Hint, Severity.Error, (room, entry) =>
                room.ObjectsOf(ObjectCategories.Target).Count() > room.ObjectsOf(ObjectCategories.Box).Count()
                    ? new[] { (room.Id, "目标数多于箱数") }
                    : Enumerable.Empty<(string, string)>()),

            new Rule("entry.missing", Severity.Hint, Severity.Error, (room, entry) =>
                entry.HasValue ? Enumerable.Empty<(string, string)>() : new[] { (room.Id, "缺少主入口") }),
        };

        public static readonly IReadOnlyList<DraftRule> DraftRules = new List<DraftRule>
        {
            new DraftRule("recovery.none", Severity.Hint, Severity.Error, d =>
                d.RecoveryItems.Count == 0 ? new[] { (d.Room.Id, "本房没有恢复道具") } : Enumerable.Empty<(string, string)>()),

            new DraftRule("recovery.duration", Severity.Error, Severity.Error, d =>
                d.RecoveryItems.Where(i => !(i.Duration > 0)).Select(i => (i.Id, "恢复道具时长必须大于 0"))),

            new DraftRule("recovery.scriptUnknown", Severity.Error, Severity.Error, UnknownScripts),

            new DraftRule("behavior.nodeUnknown", Severity.Error, Severity.Error, UnknownNodes),

            new DraftRule("id.duplicate", Severity.Error, Severity.Error, DuplicateIds),

            new DraftRule("ref.dangling", Severity.Error, Severity.Error, DanglingRefs),

            new DraftRule("door.geometry", Severity.Error, Severity.Error, DoorGeometry),

            new DraftRule("boundary.open", Severity.Hint, Severity.Error, OpenBoundary),

            new DraftRule("openable.disconnected", Severity.Warning, Severity.Warning, OpenableDisconnected),
        };

        public static List<Issue> Validate(Room room, ValidationStage stage, Cell? entry)
        {
            var issues = new List<Issue>();
            foreach (var rule in Rules)
            {
                var severity = stage == ValidationStage.Draft ? rule.Draft : rule.Publish;
                foreach (var (location, message) in rule.Check(room, entry))
                    issues.Add(new Issue(rule.Id, severity, location, message));
            }
            return issues;
        }

        public static List<Issue> ValidateDraft(RoomDraftModel draft, ValidationStage stage)
        {
            var door = draft.Doors.FirstOrDefault(d => d.Id == draft.PrimaryEntranceId && d.Role == "entry");
            Cell? entry = door == null ? (Cell?)null : door.Cell - door.Outward;
            var issues = Validate(draft.Room, stage, entry);
            foreach (var rule in DraftRules)
            {
                var severity = stage == ValidationStage.Draft ? rule.Draft : rule.Publish;
                foreach (var (location, message) in rule.Check(draft))
                    issues.Add(new Issue(rule.Id, severity, location, message));
            }
            return issues;
        }

        public static Issue ModelFootprintIssue(string assetId, float sizeX, float sizeZ)
        {
            if (Math.Abs(sizeX - 1f) <= FootprintTolerance && Math.Abs(sizeZ - 1f) <= FootprintTolerance) return null;
            return new Issue(ModelFootprintRuleId, Severity.Error, assetId, "单格模型水平包围盒必须为 1×1 格，当前为 " + sizeX + "×" + sizeZ);
        }

        public static bool HasErrors(IEnumerable<Issue> issues) => issues.Any(i => i.Severity == Severity.Error);

        static IEnumerable<(string, string)> UnknownScripts(RoomDraftModel d)
        {
            var known = new HashSet<string>(Registries.BuiltinRecoveryScripts, StringComparer.Ordinal);
            foreach (var a in d.Assets)
                if (a.Kind == AssetKinds.Script) known.Add(a.Id);
            foreach (var item in d.RecoveryItems)
                if (item.ScriptRef == null || !known.Contains(item.ScriptRef))
                    yield return (item.Id, "恢复脚本未知：" + item.ScriptRef);
        }

        static IEnumerable<(string, string)> UnknownNodes(RoomDraftModel d)
        {
            var graphs = d.Graphs.Concat(d.Templates.Values.SelectMany(t => t.Graphs));
            foreach (var graph in graphs)
                foreach (var node in graph.Nodes)
                    if (!Registries.BehaviorNodes.Contains(node.Kind))
                        yield return (graph.Id + "/" + node.Id, "行为节点类型未知：" + node.Kind);
        }

        static IEnumerable<(string, string)> DuplicateIds(RoomDraftModel d)
        {
            var room = d.Room;
            var all = new List<string>();
            all.AddRange(room.Objects.Select(o => o.Id));
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                {
                    string entity = room.TerrainAt(new Cell(x, y)).EntityId;
                    if (entity != null) all.Add(entity);
                }
            all.AddRange(d.RecoveryItems.Select(i => i.Id));
            all.AddRange(d.Graphs.Select(g => g.Id));
            all.AddRange(d.Instances.Select(i => i.Id));
            all.AddRange(d.Decorations.Select(x => x.Id));
            foreach (var group in all.GroupBy(id => id, StringComparer.Ordinal).Where(g => g.Count() > 1))
                yield return (group.Key, "ID 重复：" + group.Key);
        }

        static IEnumerable<(string, string)> DanglingRefs(RoomDraftModel d)
        {
            var room = d.Room;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in room.Objects) ids.Add(o.Id);
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                {
                    string entity = room.TerrainAt(new Cell(x, y)).EntityId;
                    if (entity != null) ids.Add(entity);
                }
            foreach (var i in d.Instances) ids.Add(i.Id);
            foreach (var i in d.RecoveryItems) ids.Add(i.Id);
            foreach (var g in d.Graphs) ids.Add(g.Id);
            foreach (var i in d.Instances)
                if (!d.Templates.ContainsKey(i.TemplateId ?? "")) yield return (i.Id, "模板不存在：" + i.TemplateId);
            foreach (var g in d.Graphs.Concat(d.Templates.Values.SelectMany(t => t.Graphs)))
            {
                if (g.OwnerId != null && !ids.Contains(g.OwnerId) && !d.Templates.ContainsKey(g.OwnerId))
                    yield return (g.Id, "行为图所有者不存在：" + g.OwnerId);
                var nodeIds = new HashSet<string>(g.Nodes.Select(n => n.Id), StringComparer.Ordinal);
                foreach (var e in g.Edges)
                    if (!nodeIds.Contains(e.FromNode) || !nodeIds.Contains(e.ToNode))
                        yield return (g.Id, "连线引用了不存在的节点：" + e.FromNode + " → " + e.ToNode);
            }
            foreach (var key in d.ObjectStates.Keys)
                if (!ids.Contains(key)) yield return (key, "对象状态表的对象不存在：" + key);
            if (d.UiImageAssetId != null && !d.Assets.Any(a => a.Id == d.UiImageAssetId))
                yield return (d.UiImageAssetId, "界面图资源不存在：" + d.UiImageAssetId);
            if (d.PrimaryEntranceId != null && !d.Doors.Any(x => x.Id == d.PrimaryEntranceId))
                yield return (d.PrimaryEntranceId, "主入口门不存在：" + d.PrimaryEntranceId);
        }

        static IEnumerable<(string, string)> DoorGeometry(RoomDraftModel draft)
        {
            foreach (var door in draft.Doors)
            {
                bool valid = RotationSteps.DirectionIndex(door.Outward) >= 0
                    && draft.Room.TerrainAt(door.Cell).CategoryId == TerrainCategories.Door
                    && draft.Room.TerrainAt(door.Cell + door.Outward).CategoryId == TerrainCategories.Void
                    && draft.Room.TerrainAt(door.Cell - door.Outward).CategoryId == TerrainCategories.Floor;
                if (!valid) yield return (door.Id, "门朝向不可通行");
            }
        }

        static IEnumerable<(string, string)> OpenBoundary(RoomDraftModel draft)
        {
            var openings = new HashSet<Cell>(draft.Doors.Select(d => d.Cell));
            var room = draft.Room;
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                {
                    var cell = new Cell(x, y);
                    if (room.TerrainAt(cell).CategoryId != TerrainCategories.Floor || openings.Contains(cell)) continue;
                    if (Cell.SearchOrder.Any(d => room.TerrainAt(cell + d).CategoryId == TerrainCategories.Void))
                        yield return (cell.ToString(), "边界缺墙，请补墙或设置朝外的门");
                }
        }

        static IEnumerable<(string, string)> OpenableDisconnected(RoomDraftModel d)
        {
            var room = d.Room;
            bool any = false;
            for (int y = 0; y < room.Height && !any; y++)
                for (int x = 0; x < room.Width && !any; x++)
                    any = room.TerrainAt(new Cell(x, y)).CategoryId == TerrainCategories.Openable;
            if (!any) yield break;
            var spawn = room.ObjectsOf(ObjectCategories.Spawn).FirstOrDefault();
            if (spawn == null) yield break;
            var seen = new HashSet<Cell> { spawn.Cell };
            var queue = new Queue<Cell>();
            queue.Enqueue(spawn.Cell);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                foreach (var dir in Cell.SearchOrder)
                {
                    var next = cell + dir;
                    if (!room.Inside(next) || seen.Contains(next)) continue;
                    string category = room.TerrainAt(next).CategoryId;
                    if (category != TerrainCategories.Floor && category != TerrainCategories.Door) continue;
                    seen.Add(next);
                    queue.Enqueue(next);
                }
            }
            foreach (var door in d.Doors)
                if (!seen.Contains(door.Cell))
                    yield return (door.Id, "可开合地形封闭时，出生点到 " + door.Role + " 门 " + door.Id + " 不互通");
        }
    }
}
