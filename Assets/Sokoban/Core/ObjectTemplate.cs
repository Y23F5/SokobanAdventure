/**
 * @file ObjectTemplate.cs
 * @brief 对象模板与模板实例：模板保存行为图、对象状态与默认外观，实例只保存模板 ID 加覆盖项，不拷贝模板内容。
 * ObjectTemplate(id, name, graphs, states, appearance)：模板 ID、显示名、行为图列表、对象状态表与默认外观引用，登记在对象模板注册表中。
 * TemplateInstance(id, templateId, overrides)：房内一个模板实例，ID 并入房间的统一 ID 空间，overrides 只记录与模板不同的字段。
 */
using System.Collections.Generic;

namespace Sokoban.Core
{
    public sealed class ObjectTemplate
    {
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<BehaviorGraph> Graphs { get; }
        public ObjectStateTable States { get; }
        public AppearanceRef Appearance { get; }

        public ObjectTemplate(string id, string name, IReadOnlyList<BehaviorGraph> graphs, ObjectStateTable states, AppearanceRef appearance)
        {
            Id = id;
            Name = name;
            Graphs = graphs ?? new List<BehaviorGraph>();
            States = states;
            Appearance = appearance;
        }
    }

    public sealed class TemplateInstance
    {
        public string Id { get; }
        public string TemplateId { get; }
        public IReadOnlyDictionary<string, string> Overrides { get; }

        public TemplateInstance(string id, string templateId, IReadOnlyDictionary<string, string> overrides = null)
        {
            Id = id;
            TemplateId = templateId;
            Overrides = overrides ?? new Dictionary<string, string>();
        }
    }
}
