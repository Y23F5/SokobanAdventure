/**
 * @file RoomProgress.cs
 * @brief RoomProgress 保存单房运行状态：房间视图、世界原点、恢复会话（含箱位）、已解开记录与对象状态表。
 * RoomProgress(room, origin)：按房间初始布局建立恢复会话，箱位以房内坐标按箱 ID 保存。
 * Boxes：会话里的当前箱位，推动与恢复都直接改它。Solved：经 PushRules.Solved 判定当前箱位是否解开。
 * RecordSolved()：当前已解开时记入 EverSolved，恢复不清除该记录。
 * Item：房间的第一个恢复道具，没有时为空。
 * Rebuild(player)：以当前箱位与给定玩家格重建恢复会话，供开始恢复与读档重建使用。
 */
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;

namespace Sokoban
{
    internal sealed class RoomProgress
    {
        internal readonly ValidatedRoom Validated;
        internal readonly Cell Origin;
        internal RecoverySession Session;
        internal bool EverSolved;
        internal readonly HashSet<string> PushedSinceRecovery = new HashSet<string>(System.StringComparer.Ordinal);
        internal readonly Dictionary<string, bool> Visibility = new Dictionary<string, bool>(System.StringComparer.Ordinal);
        internal readonly Dictionary<string, string> ObjectStates = new Dictionary<string, string>();

        internal RoomProgress(ValidatedRoom room, Cell origin)
        {
            Validated = room;
            Origin = origin;
            var boxes = room.Room.ObjectsOf(ObjectCategories.Box).ToDictionary(b => b.Id, b => b.Cell);
            Session = new RecoverySession(room.Room, boxes, room.Spawn.Cell, room.EntryBuffer);
        }

        internal Dictionary<string, Cell> Boxes => Session.Boxes;
        internal bool Solved => PushRules.Solved(Validated.Room, Session.Boxes);
        internal RecoveryItem Item => Validated.RecoveryItems.FirstOrDefault();
        internal void RecordSolved() { if (Solved) EverSolved = true; }
        internal RecoverySession Rebuild(Cell player)
        {
            Session.Place(player);
            return Session;
        }
    }
}
