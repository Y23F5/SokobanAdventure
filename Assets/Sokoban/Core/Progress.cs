/**
 * @file Progress.cs
 * @brief 玩家进度的两份独立规则数据：探索集合记录到过的房间，道具背包记录已获得的道具、当前选中项与待确认的全屏通知。
 * ExplorationSet.Visit(roomId)：记录房间，首次到访返回 true，重复到访返回 false。
 * ExplorationSet.Contains(roomId)、Rooms：查询与按登记顺序枚举已到过的房间。
 * ExplorationSet.Restore(rooms)：读档重建，房间 ID 重复抛 FormatException。
 * ItemBag.Grant(itemId)：获得道具，新道具自动成为选中项并进入待确认状态，已有道具返回 false 且不改变任何状态。
 * ItemBag.Accept()：确认全屏通知，只清除待确认项，不改变背包与选中项。
 * ItemBag.Cycle(direction)：按方向的符号在背包内循环走一步，空背包不动。
 * ItemBag.Restore(items, selected, pending)：读档重建，道具重复、选中项或待确认项不在背包内、背包非空却没有选中项均抛 FormatException。
 * PlayProgress.Enter(roomId, item)：进入房间，首次到访记入探索集合，房里有恢复道具时发放；返回本次是否为首次到访。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class ExplorationSet
    {
        readonly List<string> rooms = new List<string>();
        readonly HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        public IReadOnlyList<string> Rooms => rooms;

        public bool Contains(string roomId) => roomId != null && seen.Contains(roomId);

        public bool Visit(string roomId)
        {
            if (string.IsNullOrEmpty(roomId)) throw new ArgumentException("房间 ID 不能为空", nameof(roomId));
            if (!seen.Add(roomId)) return false;
            rooms.Add(roomId);
            return true;
        }

        public static ExplorationSet Restore(IEnumerable<string> roomIds)
        {
            var set = new ExplorationSet();
            foreach (var id in roomIds)
                if (string.IsNullOrEmpty(id) || !set.Visit(id)) throw new FormatException("探索集合含空值或重复房间：" + id);
            return set;
        }
    }

    public sealed class ItemBag
    {
        readonly List<string> items = new List<string>();

        public IReadOnlyList<string> Items => items;
        public string Selected { get; private set; }
        public string Pending { get; private set; }

        public bool Grant(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) throw new ArgumentException("道具 ID 不能为空", nameof(itemId));
            if (items.Contains(itemId)) return false;
            items.Add(itemId);
            Selected = Pending = itemId;
            return true;
        }

        public void Accept() => Pending = null;

        public void Cycle(int direction)
        {
            if (items.Count == 0 || direction == 0) return;
            int index = items.IndexOf(Selected);
            Selected = items[(index + Math.Sign(direction) + items.Count) % items.Count];
        }

        public static ItemBag Restore(IEnumerable<string> ids, string selected, string pending)
        {
            var bag = new ItemBag();
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id) || bag.items.Contains(id)) throw new FormatException("背包含空值或重复道具：" + id);
                bag.items.Add(id);
            }
            if (bag.items.Count > 0 && (selected == null || !bag.items.Contains(selected))) throw new FormatException("选中项不在背包内");
            if (bag.items.Count == 0 && selected != null) throw new FormatException("空背包不应有选中项");
            if (pending != null && !bag.items.Contains(pending)) throw new FormatException("待确认项不在背包内");
            bag.Selected = selected;
            bag.Pending = pending;
            return bag;
        }
    }

    public sealed class PlayProgress
    {
        public ExplorationSet Exploration { get; } = new ExplorationSet();
        public ItemBag Bag { get; } = new ItemBag();

        public bool Enter(string roomId, RecoveryDevice device)
        {
            bool first = Exploration.Visit(roomId);
            if (first && device != null) Bag.Grant(roomId);
            return first;
        }
    }
}
