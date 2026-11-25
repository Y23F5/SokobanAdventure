/**
 * @file Registry.cs
 * @brief Registry<T> 是按字符串 ID 查找的通用注册表，只存类型描述，不存运行状态。
 * Register(id, entry)：登记一条类型描述，ID 为空或重复时抛 ArgumentException。
 * Get(id)：按 ID 取条目，未知 ID 抛 UnknownIdException，不默认放行也不静默跳过。
 * TryGet(id, out entry)：不抛异常的查找，供需要自行处理缺失的调用方使用。
 * Contains(id)：判断是否已登记。
 * Ids：按登记顺序返回全部 ID，保证遍历顺序稳定。
 * UnknownIdException：携带注册表名与未知 ID 的异常。
 */
using System;
using System.Collections.Generic;

namespace Sokoban.Core
{
    public sealed class UnknownIdException : Exception
    {
        public string RegistryName { get; }
        public string Id { get; }

        public UnknownIdException(string registryName, string id)
            : base($"注册表 {registryName} 中没有 ID 为 '{id}' 的条目")
        {
            RegistryName = registryName;
            Id = id;
        }
    }

    public sealed class Registry<T> where T : class
    {
        readonly string name;
        readonly Dictionary<string, T> entries = new Dictionary<string, T>(StringComparer.Ordinal);
        readonly List<string> order = new List<string>();

        public Registry(string name) { this.name = name; }

        public IReadOnlyList<string> Ids => order;

        public void Register(string id, T entry)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("注册 ID 不能为空", nameof(id));
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            if (entries.ContainsKey(id)) throw new ArgumentException($"注册表 {name} 已存在 ID '{id}'", nameof(id));
            entries[id] = entry;
            order.Add(id);
        }

        public bool Contains(string id) => id != null && entries.ContainsKey(id);

        public bool TryGet(string id, out T entry)
        {
            entry = null;
            return id != null && entries.TryGetValue(id, out entry);
        }

        public T Get(string id)
        {
            if (id == null || !entries.TryGetValue(id, out var entry)) throw new UnknownIdException(name, id);
            return entry;
        }
    }
}
