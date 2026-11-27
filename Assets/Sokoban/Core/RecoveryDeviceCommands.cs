/**
 * @file RecoveryDeviceCommands.cs
 * @brief 创建可提交给 RoomDraft 的恢复装置编辑命令。
 * DefaultGraph(kind, ownerId)：为各装置类型创建含恢复开始与结束入口的空行为图。
 * SetGraph(draft, graph)：新增或更新当前装置的行为图，按图 ID 登记并支持撤销。
 * SetDetails(draft, duration, itemName, itemDescription, itemAcquiredHint)：更新时长和道具文字，保留装置身份、位置、脚本与参数。
 * SetParam(draft, key, value)：复制参数表，更新一个有限数值，撤销时恢复旧参数表。
 * Place(draft, device, replaceExisting)：检查墙外锚点和唯一 ID，经明确授权才替换房间现有装置。
 * Move(draft, anchor)：更新现有装置的锚点，保留其余配置。
 * Rotate(draft, quarterTurns)：按四分之一圈增量旋转现有装置。
 * Delete(draft)：移除现有装置和所属图，释放 ID；替换为不同 ID 时同样清理，撤销恢复。
 * RequireDevice(device)：缺少装置时拒绝编辑。
 * Edit(draft, change)：保存当前草稿持有者和装置变换。
 * Apply()、Revert()：从当前草稿原子替换装置，撤销恢复整份旧装置。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public static class RecoveryDeviceCommands
    {
        public static BehaviorGraph DefaultGraph(RecoveryDeviceKind kind, string ownerId)
        {
            if (!Enum.IsDefined(typeof(RecoveryDeviceKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(ownerId)) throw new ArgumentException("装置 ID 不能为空", nameof(ownerId));
            return new BehaviorGraph(Guid.NewGuid().ToString("N"), ownerId, new[]
            {
                new BehaviorNode(Guid.NewGuid().ToString("N"), TriggerIds.RecoveryStart),
                new BehaviorNode(Guid.NewGuid().ToString("N"), TriggerIds.RecoveryEnd)
            }, Array.Empty<BehaviorEdge>());
        }

        public static ICommand SetGraph(DraftHolder draft, BehaviorGraph graph) => new GraphEdit(draft, graph);

        sealed class GraphEdit : ICommand
        {
            readonly DraftHolder holder;
            readonly BehaviorGraph requested;
            BehaviorGraph previous, written;
            int index;

            public GraphEdit(DraftHolder holder, BehaviorGraph graph)
            {
                this.holder = holder ?? throw new ArgumentNullException(nameof(holder));
                requested = graph ?? throw new ArgumentNullException(nameof(graph));
            }

            public void Apply()
            {
                var draft = holder.Draft;
                var device = RequireDevice(draft.RecoveryDevice);
                if (requested.OwnerId != null && requested.OwnerId != device.Id)
                    throw new InvalidOperationException("行为图属于其他对象");
                string id = requested.Id;
                if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("行为图 ID 不能为空");
                previous = draft.Graphs.FirstOrDefault(g => g.Id == id);
                if (previous != null && previous.OwnerId != device.Id)
                    throw new InvalidOperationException("行为图属于其他对象");
                if (previous == null && draft.Room.Ids.Contains(id))
                    throw new ArgumentException("行为图 ID 已存在：" + id);
                written = new BehaviorGraph(id, device.Id, requested.Nodes, requested.Edges);
                index = previous == null ? draft.Graphs.Count : draft.Graphs.IndexOf(previous);
                if (previous == null) { draft.Room.Ids.Add(id, "graph"); draft.Graphs.Add(written); }
                else draft.Graphs[index] = written;
            }

            public void Revert()
            {
                var draft = holder.Draft;
                if (previous == null) { draft.Graphs.Remove(written); draft.Room.Ids.Remove(written.Id); }
                else draft.Graphs[index] = previous;
            }
        }
        public static ICommand Place(DraftHolder draft, RecoveryDevice device, bool replaceExisting = false)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            return new Edit(draft, current =>
            {
                if (current != null && !replaceExisting) throw new InvalidOperationException("本房已有恢复装置");
                return device;
            });
        }

        public static ICommand Move(DraftHolder draft, Cell anchor) =>
            new Edit(draft, current => RequireDevice(current).WithPlacement(anchor, current.Rotation));

        public static ICommand Rotate(DraftHolder draft, int quarterTurns) =>
            new Edit(draft, current => RequireDevice(current).WithPlacement(current.Anchor,
                RotationSteps.Turn(current.Rotation, quarterTurns)));

        public static ICommand Delete(DraftHolder draft) =>
            new Edit(draft, current =>
            {
                RequireDevice(current);
                return null;
            });

        public static ICommand SetDetails(DraftHolder draft, float duration, string itemName,
            string itemDescription, string itemAcquiredHint) =>
            new Edit(draft, current =>
            {
                var device = RequireDevice(current);
                return new RecoveryDevice(device.Id, device.Kind, device.Anchor, device.Rotation, device.ScriptRef,
                    duration, device.Params, itemName, itemDescription, itemAcquiredHint);
            });

        public static ICommand SetParam(DraftHolder draft, string key, float value)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("参数名不能为空", nameof(key));
            return new Edit(draft, current =>
            {
                var device = RequireDevice(current);
                var parameters = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (var pair in device.Params) parameters.Add(pair.Key, pair.Value);
                parameters[key] = value;
                return new RecoveryDevice(device.Id, device.Kind, device.Anchor, device.Rotation, device.ScriptRef,
                    device.Duration, parameters, device.ItemName, device.ItemDescription, device.ItemAcquiredHint);
            });
        }

        static RecoveryDevice RequireDevice(RecoveryDevice device) =>
            device ?? throw new InvalidOperationException("缺少恢复装置");

        sealed class Edit : ICommand
        {
            readonly DraftHolder holder;
            readonly Func<RecoveryDevice, RecoveryDevice> change;
            RecoveryDevice previous;
            List<(int index, BehaviorGraph graph)> removed;

            public Edit(DraftHolder draft, Func<RecoveryDevice, RecoveryDevice> change)
            {
                holder = draft ?? throw new ArgumentNullException(nameof(draft));
                this.change = change;
            }

            public void Apply()
            {
                var draft = holder.Draft;
                var existing = draft.RecoveryDevice;
                var next = change(existing);
                if (next != null && !RecoveryDeviceRules.IsValidAnchor(draft.Room, next.Anchor))
                    throw new InvalidOperationException("恢复装置必须紧邻房间边缘的墙外格");

                var stale = existing != null && (next == null || next.Id != existing.Id)
                    ? draft.Graphs.Select((graph, index) => (index, graph)).Where(pair => pair.graph.OwnerId == existing.Id).ToList()
                    : new List<(int index, BehaviorGraph graph)>();
                draft.ReplaceRecoveryDevice(next);
                foreach (var entry in stale) { draft.Graphs.Remove(entry.graph); draft.Room.Ids.Remove(entry.graph.Id); }
                removed = stale;
                previous = existing;
            }

            public void Revert()
            {
                var draft = holder.Draft;
                draft.ReplaceRecoveryDevice(previous);
                foreach (var entry in removed)
                {
                    draft.Room.Ids.Add(entry.graph.Id, "graph");
                    draft.Graphs.Insert(entry.index, entry.graph);
                }
            }
        }
    }
}
