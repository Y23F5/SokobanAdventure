/**
 * @file FunctionDraft.cs
 * @brief 对象模板的创作入口：把带行为图或对象状态的自定义物件保存为对象模板，实例只存模板 ID 加覆盖项，不拷贝模板内容，经 Core.RoomDraftModel 与 Commit 工作。
 * RoomDraft.CanDefineObject：所选是自定义物件、不是模板实例，且已有行为图或对象状态表。
 * RoomDraft.DefineObject(name)：把所选物件的行为图、对象状态表与外观存为对象模板；图与节点换新 ID 并去掉所有者，指向自身的显式目标改为默认目标，校验通过后经一条可撤销命令登记，不改动原物件。
 * RoomDraft.SelectDefinition(id)：选择对象模板作为放置画刷，类别为自定义，外观取模板默认外观，不修改房间。
 * RoomDraft.DefinitionAppearance、DefinitionStates：当前画刷模板的默认外观与对象状态表，供放置预览使用。
 * RoomDraft.DefinitionIssues(model)：校验模板名称、至少有行为图或状态表、模板图不带所有者且 ID 不重复、节点与状态引用有效、实例引用的模板存在，且实例 ID 属于房间 ID 空间。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;

namespace Sokoban
{
    public sealed partial class RoomDraft
    {
        public bool CanDefineObject
        {
            get
            {
                if (SelectedCategory != ObjectCategories.Custom || !SelectedCell.HasValue) return false;
                string owner = OwnerAt(SelectedCell.Value);
                if (owner == null || Model.Instances.Any(i => i.Id == owner)) return false;
                return Model.Graphs.Any(g => g.OwnerId == owner) || Model.ObjectStates.ContainsKey(owner);
            }
        }

        public AppearanceRef DefinitionAppearance => ObjectTemplates.Find(Model, BrushTemplate)?.Appearance;

        public ObjectStateTable DefinitionStates => ObjectTemplates.Find(Model, BrushTemplate)?.States;

        public void DefineObject(string name)
        {
            if (!CanDefineObject || string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("请选择已有行为的自定义物件，并填写模板名称");
            var model = Model;
            var room = Room;
            string owner = OwnerAt(SelectedCell.Value);
            var source = room.Objects.First(o => o.Id == owner);
            var graphs = new List<BehaviorGraph>();
            foreach (var sourceGraph in model.Graphs.Where(g => g.OwnerId == owner))
            {
                var copy = ObjectBehavior.CopyFor(sourceGraph, null);
                var nodes = copy.Nodes.Select(n => SelfTargetless(n, owner)).ToList();
                graphs.Add(new BehaviorGraph(copy.Id, null, nodes, copy.Edges));
            }
            model.ObjectStates.TryGetValue(owner, out var states);
            string id = "template-" + Guid.NewGuid().ToString("N");
            var template = new ObjectTemplate(id, name.Trim(), graphs, states, source.Appearance);
            var draft = new Dictionary<string, ObjectTemplate>(model.Templates) { [id] = template };
            string issue = DefinitionIssues(model, draft.Values).FirstOrDefault();
            if (issue != null) throw new InvalidOperationException(issue);
            var hold = AssetCopy(source.Appearance?.Id);
            Commit(new ModelEdit(() => { hold.Apply(model); model.Templates[id] = template; },
                () => { model.Templates.Remove(id); hold.Revert(model); }));
        }

        static BehaviorNode SelfTargetless(BehaviorNode node, string owner)
        {
            if (ObjectBehavior.Parameter(node, "targetObject") != owner) return node;
            return ObjectBehavior.WithParameter(ObjectBehavior.WithParameter(node, "targetObject", null), "targetRoom", null);
        }

        public void SelectDefinition(string id)
        {
            var template = ObjectTemplates.Find(Model, id) ?? throw new InvalidOperationException("当前房间没有所选对象模板");
            ClearBrush();
            BrushCategory = ObjectCategories.Custom;
            BrushTemplate = template.Id;
            BrushAppearance = template.Appearance;
        }

        public static IEnumerable<string> DefinitionIssues(RoomDraftModel model) => DefinitionIssues(model, model.Templates.Values);

        static IEnumerable<string> DefinitionIssues(RoomDraftModel model, IEnumerable<ObjectTemplate> templates)
        {
            foreach (var template in templates)
            {
                if (string.IsNullOrWhiteSpace(template.Id) || string.IsNullOrWhiteSpace(template.Name) || template.Graphs.Count == 0 && template.States == null)
                {
                    yield return "对象模板缺少名称或行为：" + template.Id;
                    continue;
                }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var graph in template.Graphs)
                {
                    if (graph.OwnerId != null) yield return "对象模板的行为图必须由实例分配所有者：" + template.Id;
                    if (string.IsNullOrWhiteSpace(graph.Id) || !seen.Add(graph.Id)) yield return template.Id + "：模板图 ID 缺失或重复";
                    foreach (string issue in ObjectBehavior.GraphIssues(graph, template.States)) yield return template.Id + "：" + issue;
                }
                foreach (string issue in ObjectStateProfile.Issues(template.States, ObjectCategories.Custom, id => model.Assets.Any(a => a.Id == id && a.Kind == AssetKinds.Model)))
                    yield return template.Id + "：" + issue;
            }
            foreach (var instance in model.Instances)
            {
                if (ObjectTemplates.Find(model, instance.TemplateId) == null) yield return "对象引用了不存在的对象模板：" + instance.Id;
                if (!model.Room.Ids.Contains(instance.Id)) yield return "模板实例的 ID 未登记到房间：" + instance.Id;
            }
        }
    }
}
