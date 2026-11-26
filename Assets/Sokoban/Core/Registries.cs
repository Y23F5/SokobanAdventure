/**
 * @file Registries.cs
 * @brief 默认注册表的集中入口：规则层与校验统一从这里取地形类别、对象类别、触发器、资源类型与行为节点类型，注册表只放类型描述，不放运行状态。
 * Registries.Terrains、Objects、Triggers、AssetKinds、BehaviorNodes：各自 CreateDefault 生成的默认注册表，进程内共用一份。
 * Registries.BuiltinRecoveryScripts：随项目发布的默认恢复脚本 ID（warehouse-lift、factory-belt、forest-decay、forest-vine），校验恢复道具的脚本引用时与房间内 script 类资源一起视为已知。
 */
using System.Collections.Generic;

namespace Sokoban.Core
{
    public static class Registries
    {
        public static Registry<TerrainCategory> Terrains { get; } = TerrainCategories.CreateDefault();
        public static Registry<ObjectCategory> Objects { get; } = ObjectCategories.CreateDefault();
        public static Registry<TriggerInfo> Triggers { get; } = TriggerIds.CreateDefault();
        public static Registry<AssetKindInfo> AssetKinds { get; } = global::Sokoban.Core.AssetKinds.CreateDefault();
        public static Registry<BehaviorNodeKind> BehaviorNodes { get; } = BehaviorNodeKinds.CreateDefault();
        public static IReadOnlyList<string> BuiltinRecoveryScripts { get; } = new[] { "warehouse-lift", "factory-belt", "forest-decay", "forest-vine" };
    }
}
