/**
 * @file RoomCoreBridge.cs
 * @brief 旧 JObject 房间到 Core 校验器的薄桥接层，只新增、暂未接入 RoomFile；Core 的异常在此转成文案，不向外抛出。
 * LoadIssues：把房间经 RoomFile.ToLegacyData 与 LegacyRoomAdapter.ToDraft 转成 Core 草稿，取 RoomValidator.LoadErrors 的同格占用与不在地板两类问题，
 * 并按旧 Validate 的文案改写；读入档规则与门朝向无关，转换前先把非法朝向字母的门换成占位朝向 N，以免旧 Validate 不管的朝向问题被算作读入错误；
 * 转换阶段抛出的 FormatException、ArgumentException 或读取旧字段时的 InvalidDataException 各转成一条文案，结果为立即求值的列表。
 * StructureIssues：按旧 StructureIssues 前半段的顺序依次给出缺出生点、恢复位置重复、门循环、找不到主入口；
 * 缺出生点、门几何错误的门 ID 与缺主入口取自 Core 的 RoomValidator.StructureErrors，恢复位置重复与门循环交给 RoomLegacyChecks，
 * 其中门几何错误的门 ID 按 door.geometry 结果的定位取得；转换失败（如门朝向字母非法）时，缺出生点与缺主入口改为直接读 JObject 判定，几何集合为空，门朝向问题由 DoorIssues 自行报告。
 * 主入口内侧留空由 DoorIssues 负责，故不采用 Core 的 entry.occupied；边界缺口、行为图与定义目录问题仍留旧侧，不在此处理。
 * Describe：按规则 ID 把 Core 的定位与旧文案拼成与旧 Validate 一致的句子。
 * TryStructure：尝试取 Core 结构错误，转换失败返回 null。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace Sokoban
{
    public static class RoomCoreBridge
    {
        public static IEnumerable<string> LoadIssues(JObject room)
        {
            try
            {
                var data = RoomFile.ToLegacyData(room);
                data.Doors = data.Doors.Select(d => RoomLegacyChecks.IsLegalDirection(d.Direction) ? d : new Sokoban.Core.LegacyDoor(d.Id, d.Cell, "N", d.Role)).ToArray();
                var draft = Sokoban.Core.LegacyRoomAdapter.ToDraft(data);
                return Sokoban.Core.RoomValidator.LoadErrors(draft).Select(Describe).ToList();
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is InvalidDataException)
            {
                return new List<string> { e.Message };
            }
        }

        public static IEnumerable<string> StructureIssues(JObject room)
        {
            string level = (string)room["levelId"];
            var core = TryStructure(room);
            var result = new List<string>();
            if (core != null ? core.Any(i => i.RuleId == "spawn.missing") : room["spawn"] == null)
                result.Add(level + " 需要放置玩家出生点");
            result.AddRange(RoomLegacyChecks.RecoveryDuplicateIssues(room));
            var badGeometry = new HashSet<string>(core == null
                ? Enumerable.Empty<string>()
                : core.Where(i => i.RuleId == "door.geometry").Select(i => i.Location));
            result.AddRange(RoomLegacyChecks.DoorIssues(room, badGeometry));
            bool entryMissing = core != null
                ? core.Any(i => i.RuleId == "entry.missing")
                : !room["doors"].Any(d => (string)d["id"] == (string)room["primaryEntranceId"] && (string)d["role"] == "entry");
            if (entryMissing) result.Add(level + " 找不到主入口 " + (string)room["primaryEntranceId"]);
            return result;
        }

        static List<Sokoban.Core.Issue> TryStructure(JObject room)
        {
            try
            {
                return Sokoban.Core.RoomValidator.StructureErrors(Sokoban.Core.LegacyRoomAdapter.ToDraft(RoomFile.ToLegacyData(room)));
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException)
            {
                return null;
            }
        }

        static string Describe(Sokoban.Core.Issue issue)
        {
            switch (issue.RuleId)
            {
                case "object.sharedCell": return issue.Location + " 与其他对象占用同一格";
                case "object.notOnFloor": return issue.Location + " 越界或位于墙内";
                default: return issue.Location + " " + issue.Message;
            }
        }
    }
}
