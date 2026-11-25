/**
 * @file ObjectCategory.cs
 * @brief ObjectCategory 描述对象层的一种类别（箱、目标、出生点、自定义物件）的规则，ObjectCategories 提供默认注册表。
 * ObjectCategory(...)：构造类别，声明可推动、参与目标匹配、外观槽位与发布级数量约束。
 * ObjectCategories.CreateDefault()：登记 box、target、spawn、custom 四个类别。
 * 对象只能放在承载对象的地形格上，装饰在房间外，不属于对象层。
 */
namespace Sokoban.Core
{
    public sealed class ObjectCategory
    {
        public string Id { get; }
        public bool Pushable { get; }
        public bool MatchesTarget { get; }
        public string AppearanceSlot { get; }
        public int MinCount { get; }
        public int MaxCount { get; }

        public ObjectCategory(string id, bool pushable, bool matchesTarget, string appearanceSlot,
            int minCount = 0, int maxCount = int.MaxValue)
        {
            Id = id;
            Pushable = pushable;
            MatchesTarget = matchesTarget;
            AppearanceSlot = appearanceSlot;
            MinCount = minCount;
            MaxCount = maxCount;
        }
    }

    public static class ObjectCategories
    {
        public const string Box = "box";
        public const string Target = "target";
        public const string Spawn = "spawn";
        public const string Custom = "custom";

        public static Registry<ObjectCategory> CreateDefault()
        {
            var registry = new Registry<ObjectCategory>("object");
            registry.Register(Box, new ObjectCategory(Box, true, true, "box"));
            registry.Register(Target, new ObjectCategory(Target, false, true, "target", 1));
            registry.Register(Spawn, new ObjectCategory(Spawn, false, false, null, 1, 1));
            registry.Register(Custom, new ObjectCategory(Custom, false, false, "custom"));
            return registry;
        }
    }
}
