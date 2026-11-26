/**
 * @file ObjectStateProfile.cs
 * @brief 对象状态表的创作入口，数据一律读写 Core.ObjectStateTable，转换挂在 TriggerIds 上，残留物是纯表现，不再有 restore 触发与恢复动画字段。
 * ObjectStateProfile.Triggers：状态转换可选的触发器 ID，firstPush、roomSolved、recoveryEnd，恢复完成对应 recoveryEnd。
 * ObjectStateProfile.StatesOf(model, ownerId)：取所有者的状态表，房间自有表优先，对象模板实例回落到模板的状态表，没有时为空。
 * ObjectStateProfile.StateLook(state)：状态名对应的内置外观 ID，tree 对应 ForestTree，logs 对应 Logs，其余状态名原样作外观 ID，空表示沿用对象自身外观。
 * ObjectStateProfile.Prefab(id)：内置外观的 Prefab 路径，导入模型返回空。
 * ObjectStateProfile.ForestTable()：森林树木的状态表，tree 经 firstPush 变为 logs 并留下树桩残留物，logs 经 recoveryEnd 回到 tree。
 * ObjectStateProfile.Issues(table, categoryId, known)：校验初始状态、转换两端、触发器已登记、同状态同触发器不重复、firstPush 只用于箱子、残留物外观可识别，known 判断外观 ID 是否为本房模型资源。
 * RoomDraft.CanEditStateProfile、SetStateTable(table)：所选为箱子或自定义物件且不是模板实例时，经一条可撤销命令写入或移除对象状态表，残留物若是本房模型资源则一并登记。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;

namespace Sokoban
{
    public static class ObjectStateProfile
    {
        public static readonly string[] Triggers = { TriggerIds.FirstPush, TriggerIds.RoomSolved, TriggerIds.RecoveryEnd };

        public static string Prefab(string id) => AppearanceLibrary.Find(id)?.Prefab();

        public static ObjectStateTable StatesOf(RoomDraftModel model, string ownerId)
        {
            if (ownerId == null) return null;
            if (model.ObjectStates.TryGetValue(ownerId, out var table)) return table;
            var instance = model.Instances.FirstOrDefault(i => i.Id == ownerId);
            return instance == null ? null : ObjectTemplates.Find(model, instance.TemplateId)?.States;
        }

        public static string StateLook(string state) => state == "tree" ? "ForestTree" : state == "logs" ? "Logs" : state;

        public static ObjectStateTable ForestTable() => new ObjectStateTable("tree", new[] { "tree", "logs" }, new[]
        {
            new StateTransition("tree", TriggerIds.FirstPush, "logs", new AppearanceRef("TreeStump")),
            new StateTransition("logs", TriggerIds.RecoveryEnd, "tree")
        });

        public static IEnumerable<string> Issues(ObjectStateTable table, string categoryId, Func<string, bool> known)
        {
            if (table == null) yield break;
            if (table.States.Count == 0 || !table.States.Contains(table.Initial)) yield return "对象状态需要初始状态和状态列表";
            var keys = new HashSet<(string, string)>();
            foreach (var t in table.Transitions)
            {
                if (!table.States.Contains(t.From) || !table.States.Contains(t.To)) yield return "状态转换的两端必须是已有状态";
                if (!Registries.Triggers.Contains(t.Trigger)) yield return "未知触发器：" + t.Trigger;
                else if (!keys.Add((t.From, t.Trigger))) yield return "同一状态的同一触发器只能有一条转换：" + t.From + "/" + t.Trigger;
                if (t.Trigger == TriggerIds.FirstPush && categoryId != ObjectCategories.Box) yield return "firstPush 只适用于可推动的箱子";
                if (t.Remnant != null && Prefab(t.Remnant.Id) == null && !known(t.Remnant.Id)) yield return "残留物外观无效：" + t.Remnant.Id;
            }
        }
    }

    public sealed partial class RoomDraft
    {
        public bool CanEditStateProfile
        {
            get
            {
                string category = SelectedCategory;
                if (category != ObjectCategories.Box && category != ObjectCategories.Custom) return false;
                string owner = SelectedCell.HasValue ? OwnerAt(SelectedCell.Value) : null;
                return owner != null && !(Model.Instances.Any(i => i.Id == owner) && !Model.ObjectStates.ContainsKey(owner));
            }
        }

        public void SetStateTable(ObjectStateTable table)
        {
            if (!CanEditStateProfile) throw new InvalidOperationException("请选择一个箱子或自定义物件");
            var cell = SelectedCell.Value;
            string owner = OwnerAt(cell);
            var model = Model;
            var issue = ObjectStateProfile.Issues(table, SelectedCategory, id => AssetDefinition(id)?.Kind == AssetKinds.Model).FirstOrDefault();
            if (issue != null) throw new InvalidOperationException(issue);
            model.ObjectStates.TryGetValue(owner, out var before);
            if (ReferenceEquals(before, table)) return;
            var holds = table == null ? new List<AssetHold>() : table.Transitions.Where(t => t.Remnant != null).Select(t => AssetCopy(t.Remnant.Id)).ToList();
            Commit(new ModelEdit(() =>
            {
                foreach (var h in holds) h.Apply(model);
                if (table == null) model.ObjectStates.Remove(owner); else model.ObjectStates[owner] = table;
            }, () =>
            {
                if (before == null) model.ObjectStates.Remove(owner); else model.ObjectStates[owner] = before;
                for (int i = holds.Count - 1; i >= 0; i--) holds[i].Revert(model);
            }));
            Select(cell);
        }
    }
}
