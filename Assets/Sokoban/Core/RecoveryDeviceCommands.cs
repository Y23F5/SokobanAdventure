/**
 * @file RecoveryDeviceCommands.cs
 * @brief 创建可提交给 RoomDraft 的四种恢复装置编辑命令。
 * Place(draft, device, replaceExisting)：检查墙外锚点和唯一 ID，经明确授权才替换房间现有装置。
 * Move(draft, anchor)：更新现有装置的锚点，保留其余配置。
 * Rotate(draft, quarterTurns)：按四分之一圈增量旋转现有装置。
 * Delete(draft)：移除现有装置，释放 ID。
 * RequireDevice(device)：缺少装置时拒绝编辑。
 * Edit(draft, change)：保存当前草稿持有者和装置变换。
 * Apply()、Revert()：从当前草稿原子替换装置，撤销恢复整份旧装置。
 */
using System;

namespace Sokoban.Core
{
    public static class RecoveryDeviceCommands
    {
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

        static RecoveryDevice RequireDevice(RecoveryDevice device) =>
            device ?? throw new InvalidOperationException("缺少恢复装置");

        sealed class Edit : ICommand
        {
            readonly DraftHolder holder;
            readonly Func<RecoveryDevice, RecoveryDevice> change;
            RecoveryDevice previous;

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

                draft.ReplaceRecoveryDevice(next);
                previous = existing;
            }

            public void Revert() => holder.Draft.ReplaceRecoveryDevice(previous);
        }
    }
}