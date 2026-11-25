/**
 * @file IdSpace.cs
 * @brief IdSpace 是每房唯一的 ID 空间，对象、地形实体、行为图、恢复道具共用，全局以 (roomId, id) 寻址。
 * Add(id, kind)：登记 ID 与其种类，ID 为空或重复时抛 ArgumentException。
 * Remove(id)：移除 ID，返回是否存在，供删除与撤销时统一连带清理。
 * Contains(id)、KindOf(id)：查询是否存在及其种类，未知 ID 的 KindOf 抛 UnknownIdException。
 * Ids：按登记顺序返回全部 ID。
 */
using System;
using System.Collections.Generic;

namespace Sokoban.Core
{
    public sealed class IdSpace
    {
        readonly string roomId;
        readonly Dictionary<string, string> kinds = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly List<string> order = new List<string>();

        public IdSpace(string roomId) { this.roomId = roomId; }

        public string RoomId => roomId;
        public IReadOnlyList<string> Ids => order;
        public int Count => order.Count;

        public void Add(string id, string kind)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("ID 不能为空", nameof(id));
            if (kinds.ContainsKey(id)) throw new ArgumentException($"房间 {roomId} 中 ID '{id}' 已存在", nameof(id));
            kinds[id] = kind;
            order.Add(id);
        }

        public bool Remove(string id)
        {
            if (id == null || !kinds.Remove(id)) return false;
            order.Remove(id);
            return true;
        }

        public bool Contains(string id) => id != null && kinds.ContainsKey(id);

        public string KindOf(string id)
        {
            if (id == null || !kinds.TryGetValue(id, out var kind)) throw new UnknownIdException("ids:" + roomId, id);
            return kind;
        }
    }
}
