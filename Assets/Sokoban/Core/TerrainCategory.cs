/**
 * @file TerrainCategory.cs
 * @brief TerrainCategory 描述一种地形类别（空格、地板、墙、门、可开合地形）的规则，TerrainCategories 提供默认注册表。
 * TerrainCategory(...)：构造类别，声明承载对象、外观槽位、状态表与通行状态。
 * IsWalkable(state)：无状态机的类别返回固定通行性；有状态机的类别仅在通行状态下可通行。
 * TerrainCategories.CreateDefault()：登记 void、floor、wall、door、openable 五个类别。
 */
using System;

namespace Sokoban.Core
{
    public sealed class TerrainCategory
    {
        public string Id { get; }
        public bool CarriesObjects { get; }
        public string AppearanceSlot { get; }
        public string[] States { get; }
        public string InitialState { get; }

        readonly bool baseWalkable;
        readonly string walkableState;

        public TerrainCategory(string id, bool walkable, bool carriesObjects, string appearanceSlot,
            string[] states = null, string initialState = null, string walkableState = null)
        {
            Id = id;
            baseWalkable = walkable;
            CarriesObjects = carriesObjects;
            AppearanceSlot = appearanceSlot;
            States = states ?? Array.Empty<string>();
            InitialState = initialState;
            this.walkableState = walkableState;
        }

        public bool HasStates => States.Length > 0;

        public bool IsWalkable(string state) => HasStates ? state == walkableState : baseWalkable;
    }

    public static class TerrainCategories
    {
        public const string Void = "void";
        public const string Floor = "floor";
        public const string Wall = "wall";
        public const string Door = "door";
        public const string Openable = "openable";
        public const string Closed = "closed";
        public const string Open = "open";

        public static Registry<TerrainCategory> CreateDefault()
        {
            var registry = new Registry<TerrainCategory>("terrain");
            registry.Register(Void, new TerrainCategory(Void, false, false, null));
            registry.Register(Floor, new TerrainCategory(Floor, true, true, "floor"));
            registry.Register(Wall, new TerrainCategory(Wall, false, false, "wall"));
            registry.Register(Door, new TerrainCategory(Door, true, false, "door"));
            registry.Register(Openable, new TerrainCategory(Openable, false, false, "openable",
                new[] { Closed, Open }, Closed, Open));
            return registry;
        }
    }
}
