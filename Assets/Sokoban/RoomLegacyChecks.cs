/**
 * @file RoomLegacyChecks.cs
 * @brief 旧 RoomFile 中 Core 无法表达的结构检查，抽成不依赖 Core 的纯函数，规则、顺序与文案同旧实现。
 * IsLegalDirection 判断门朝向是否为 N、E、S、W 之一的单个字母，先判空和长度再查字母表；
 * RecoveryCells 按箱子顺序把恢复格收进集合，格式非法时由 RoomFile.Cell 抛出；
 * RecoveryCellIssues 对应旧 Validate 的箱子恢复格检查，逐个箱子判断恢复格是否为地板，遇到第一个无效即给出固定文案并停止，与旧实现抛出首个错误一致；
 * RecoveryDuplicateIssues 对应旧 StructureIssues 的恢复位置重复检查，用集合记录已出现的恢复格，重复时按“层 箱子 恢复位置重复 格”输出；
 * DoorIssues 对应旧 StructureIssues 的门循环，朝向字母非法或门 ID 在传入的几何错误集合中时先报需要朝外的文案并跳过该门，
 * 其余门若为主入口，则在内侧格被除出生点外的任何实体、装饰物、门或箱子恢复格占用时报主入口内侧需要留空。
 */
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public static class RoomLegacyChecks
    {
        public static bool IsLegalDirection(string direction) =>
            direction != null && direction.Length == 1 && "NESW".Contains(direction);

        public static HashSet<Vector2Int> RecoveryCells(JObject room)
        {
            var cells = new HashSet<Vector2Int>();
            foreach (var box in room["boxes"]) cells.Add(RoomFile.Cell(box["recoveryCell"]));
            return cells;
        }

        public static IEnumerable<string> RecoveryCellIssues(JObject room)
        {
            foreach (var box in room["boxes"])
            {
                if (!RoomFile.Floor(room, RoomFile.Cell(box["recoveryCell"])))
                {
                    yield return "箱子恢复格无效";
                    yield break;
                }
            }
        }

        public static IEnumerable<string> RecoveryDuplicateIssues(JObject room)
        {
            string level = (string)room["levelId"];
            var recovery = new HashSet<Vector2Int>();
            foreach (var box in room["boxes"])
            {
                var cell = RoomFile.Cell(box["recoveryCell"]);
                if (!recovery.Add(cell)) yield return $"{level} {box["id"]} 恢复位置重复 {cell}";
            }
        }

        public static IEnumerable<string> DoorIssues(JObject room, ISet<string> badGeometry)
        {
            string level = (string)room["levelId"];
            var recovery = RecoveryCells(room);
            foreach (var door in room["doors"])
            {
                var cell = RoomFile.At(door);
                if (!IsLegalDirection((string)door["direction"]) || (badGeometry != null && badGeometry.Contains((string)door["id"])))
                {
                    yield return $"{level} {cell} 门 {door["id"]} 需要朝外且内侧可通行";
                    continue;
                }
                if ((string)door["id"] == (string)room["primaryEntranceId"] &&
                    (RoomFile.Objects(room).Any(obj => !object.ReferenceEquals(obj, room["spawn"]) && RoomFile.At(obj) == cell + RoomFile.Inward(door)) || recovery.Contains(cell + RoomFile.Inward(door))))
                    yield return $"{level} {cell + RoomFile.Inward(door)} 主入口内侧需要留空";
            }
        }
    }
}
