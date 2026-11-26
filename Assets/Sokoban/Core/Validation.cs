/**
 * @file Validation.cs
 * @brief 两档校验共用同一份判定：每条规则只写一次，同时声明草稿级与发布级的严重级别，档位只决定严重级别的映射。
 * Severity：提示、警告、错误三档严重级别。
 * ValidationStage：草稿级与发布级两个档位。
 * Issue：一条校验结果，带规则 ID、定位（格子或对象 ID）与原因文字。
 * Rule：一条规则，声明规则 ID、草稿级与发布级严重级别以及判定函数。
 * RoomValidator.Validate(room, stage, entry)：运行全部规则并按档位映射严重级别，返回结果列表；object.notOnFloor 与 object.sharedCell 同样覆盖装饰物（custom 类别对象）。
 * RoomValidator.HasErrors(issues)：判断结果中是否存在错误，发布级存在错误时阻止试玩、保存与导出。
 * entry 为主入口内侧的入口缓冲格，缺主入口时传 null。
 * RoomValidator.ValidateDraft(draft, stage)：在 Validate 之上，为草稿模型追加 recovery.none（没有恢复道具）、door.geometry（门格不是门地形、外侧不是空格或内侧不是地板，两档均为错误）与 boundary.open（入口缓冲格上的出生点不算占用；非门格的地板紧邻空格或房间边缘，草稿提示、发布错误）两条规则。
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

    public static class RoomValidator
    {
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

            new Rule("box.recoveryNotFloor", Severity.Error, Severity.Error, (room, entry) =>
                room.Objects.Where(o => o.RecoveryCell.HasValue && room.TerrainAt(o.RecoveryCell.Value).CategoryId != TerrainCategories.Floor)
                    .Select(o => (o.Id, "恢复格不是地板"))),

            new Rule("box.recoveryDuplicate", Severity.Error, Severity.Error, (room, entry) =>
                room.Objects.Where(o => o.RecoveryCell.HasValue).GroupBy(o => o.RecoveryCell.Value)
                    .Where(g => g.Count() > 1).SelectMany(g => g.Skip(1).Select(o => (o.Id, "恢复格重复")))),

            new Rule("entry.missing", Severity.Hint, Severity.Error, (room, entry) =>
                entry.HasValue ? Enumerable.Empty<(string, string)>() : new[] { (room.Id, "缺少主入口") }),
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
            if (draft.RecoveryItems.Count == 0)
                issues.Add(new Issue("recovery.none", stage == ValidationStage.Draft ? Severity.Hint : Severity.Error, draft.Room.Id, "本房没有恢复道具"));
            foreach (var door2 in draft.Doors)
            {
                bool valid = draft.Room.TerrainAt(door2.Cell).CategoryId == TerrainCategories.Door
                    && draft.Room.TerrainAt(door2.Cell + door2.Outward).CategoryId == TerrainCategories.Void
                    && draft.Room.TerrainAt(door2.Cell - door2.Outward).CategoryId == TerrainCategories.Floor;
                if (!valid) issues.Add(new Issue("door.geometry", Severity.Error, door2.Id, "门需要朝外且内侧可通行"));
            }
            var openings = new HashSet<Cell>(draft.Doors.Select(d => d.Cell));
            var room = draft.Room;
            for (int y = 0; y < room.Height; y++)
                for (int x = 0; x < room.Width; x++)
                {
                    var cell = new Cell(x, y);
                    if (room.TerrainAt(cell).CategoryId != TerrainCategories.Floor || openings.Contains(cell)) continue;
                    if (Cell.SearchOrder.Any(d => room.TerrainAt(cell + d).CategoryId == TerrainCategories.Void))
                        issues.Add(new Issue("boundary.open", stage == ValidationStage.Draft ? Severity.Hint : Severity.Error, cell.ToString(), "边界缺墙，请补墙或设置朝外的门"));
                }
            return issues;
        }

        public static bool HasErrors(IEnumerable<Issue> issues) => issues.Any(i => i.Severity == Severity.Error);
    }
}
