/**
 * @file IssueLocator.cs
 * @brief 校验问题到定位目标与建议工具的纯映射，供 check.locate 在检查页一键定位并进入对应放置或编辑工具，Core 不依赖界面。
 * LocateKind：定位对象的种类（房间、格、对象、门）。
 * LocateTool：建议进入的工具（无、选择、放置出生点、放置目标、画墙、设置门、添加恢复道具）。
 * LocateTarget：定位结果，含种类、对象或门或房间的 ID、格坐标（仅格类有效）与建议工具。
 * IssueLocator.Locate(issue)：按规则 ID 确定定位种类与建议工具；boundary.open 的 Location 解析为格坐标，door.geometry 与门相关规则定位到门 ID，
 * object.*、box.recovery*、entry.occupied 定位到对象 ID，其余及未知规则定位到房间且不建议工具；Location 无法解析为格坐标时退回房间定位。
 */
using System;

namespace Sokoban.Core
{
    public enum LocateKind { Room, Cell, Object, Door }

    public enum LocateTool { None, Select, PlaceSpawn, PlaceTarget, PaintWall, SetDoor, AddRecoveryItem }

    public sealed class LocateTarget
    {
        public LocateKind Kind { get; }
        public string Id { get; }
        public Cell Cell { get; }
        public LocateTool Tool { get; }

        public LocateTarget(LocateKind kind, string id, Cell cell, LocateTool tool)
        {
            Kind = kind;
            Id = id;
            Cell = cell;
            Tool = tool;
        }
    }

    public static class IssueLocator
    {
        public static LocateTarget Locate(Issue issue)
        {
            string rule = issue.RuleId;
            switch (rule)
            {
                case "spawn.missing": return new LocateTarget(LocateKind.Room, issue.Location, default(Cell), LocateTool.PlaceSpawn);
                case "target.none": return new LocateTarget(LocateKind.Room, issue.Location, default(Cell), LocateTool.PlaceTarget);
                case "entry.missing": return new LocateTarget(LocateKind.Room, issue.Location, default(Cell), LocateTool.SetDoor);
                case "recovery.none": return new LocateTarget(LocateKind.Room, issue.Location, default(Cell), LocateTool.AddRecoveryItem);
                case "door.geometry": return new LocateTarget(LocateKind.Door, issue.Location, default(Cell), LocateTool.SetDoor);
                case "boundary.open":
                    return TryCell(issue.Location, out var cell)
                        ? new LocateTarget(LocateKind.Cell, null, cell, LocateTool.PaintWall)
                        : new LocateTarget(LocateKind.Room, issue.Location, default(Cell), LocateTool.None);
            }
            if (rule.StartsWith("object.", StringComparison.Ordinal) || rule.StartsWith("box.recovery", StringComparison.Ordinal) ||
                rule == "entry.occupied" || rule == "spawn.multiple")
                return new LocateTarget(LocateKind.Object, issue.Location, default(Cell), LocateTool.Select);
            return new LocateTarget(LocateKind.Room, issue.Location, default(Cell), LocateTool.None);
        }

        static bool TryCell(string text, out Cell cell)
        {
            cell = default(Cell);
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Trim('(', ')').Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y)) return false;
            cell = new Cell(x, y);
            return true;
        }
    }
}
