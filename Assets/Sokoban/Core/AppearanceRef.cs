/**
 * @file AppearanceRef.cs
 * @brief 外观引用：规则层只保存外观 ID 与变体序号，不解释其含义，表现层按 ID 查找外观。
 * AppearanceRef(id, variant)：外观 ID 与写入数据的变体序号，变体序号不按坐标哈希。
 * Equals 与 GetHashCode 按 ID 与变体序号比较，供撤销还原与相等比较使用。
 */
using System;

namespace Sokoban.Core
{
    public sealed class AppearanceRef : IEquatable<AppearanceRef>
    {
        public string Id { get; }
        public int Variant { get; }

        public AppearanceRef(string id, int variant = 0)
        {
            Id = id;
            Variant = variant;
        }

        public bool Equals(AppearanceRef other) => other != null && other.Id == Id && other.Variant == Variant;

        public override bool Equals(object obj) => Equals(obj as AppearanceRef);

        public override int GetHashCode() => (Id?.GetHashCode() ?? 0) * 31 + Variant;
    }
}
