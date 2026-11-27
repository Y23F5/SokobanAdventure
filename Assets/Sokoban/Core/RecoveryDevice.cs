/**
 * @file RecoveryDevice.cs
 * @brief 房间恢复装置及其墙外放置规则。
 * RecoveryDevice(id, kind, anchor, rotation, scriptRef, duration, parameters, itemName, itemDescription, itemAcquiredHint)：构造不可变装置并快照参数。
 * WithPlacement(anchor, rotation)：保留 ID 和配置，返回更新位置和朝向后的装置。
 * IsValidAnchor(room, anchor)：仅允许矩形四边外紧邻的一格，不含角格。
 * Validate(draft, stage)：检查装置是否存在及锚点是否合法，缺失严重级别与出生点一致。
 */
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Sokoban.Core
{
    public enum RecoveryDeviceKind { Forklift, Arm, Tree }

    public sealed class RecoveryDevice
    {
        public string Id { get; }
        public RecoveryDeviceKind Kind { get; }
        public Cell Anchor { get; }
        public int Rotation { get; }
        public string ScriptRef { get; }
        public float Duration { get; }
        public IReadOnlyDictionary<string, float> Params { get; }
        public string ItemName { get; }
        public string ItemDescription { get; }
        public string ItemAcquiredHint { get; }

        public Cell EntryDirection => Rotation switch
        {
            0 => Cell.Up,
            1 => Cell.Right,
            2 => Cell.Down,
            _ => Cell.Left
        };

        public RecoveryDevice(string id, RecoveryDeviceKind kind, Cell anchor, int rotation,
            string scriptRef, float duration, IReadOnlyDictionary<string, float> parameters = null,
            string itemName = null, string itemDescription = null, string itemAcquiredHint = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("恢复装置 ID 不能为空", nameof(id));
            if (!Enum.IsDefined(typeof(RecoveryDeviceKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (string.IsNullOrWhiteSpace(scriptRef)) throw new ArgumentException("恢复脚本引用不能为空", nameof(scriptRef));
            if (!(duration > 0) || float.IsInfinity(duration)) throw new ArgumentOutOfRangeException(nameof(duration));

            var snapshot = new Dictionary<string, float>(StringComparer.Ordinal);
            if (parameters != null)
                foreach (var parameter in parameters)
                {
                    if (float.IsNaN(parameter.Value) || float.IsInfinity(parameter.Value))
                        throw new ArgumentException("恢复脚本参数必须是有限数值", nameof(parameters));
                    snapshot.Add(parameter.Key, parameter.Value);
                }

            Id = id;
            Kind = kind;
            Anchor = anchor;
            Rotation = RotationSteps.Validate(rotation);
            ScriptRef = scriptRef;
            Duration = duration;
            Params = new ReadOnlyDictionary<string, float>(snapshot);
            ItemName = itemName ?? "";
            ItemDescription = itemDescription ?? "";
            ItemAcquiredHint = itemAcquiredHint ?? "";
        }

        internal RecoveryDevice WithPlacement(Cell anchor, int rotation) =>
            new RecoveryDevice(Id, Kind, anchor, rotation, ScriptRef, Duration,
                Params, ItemName, ItemDescription, ItemAcquiredHint);
    }

    public static class RecoveryDeviceRules
    {
        public static bool IsValidAnchor(Room room, Cell anchor) =>
            ((anchor.X == -1 || anchor.X == room.Width) && anchor.Y >= 0 && anchor.Y < room.Height) ||
            ((anchor.Y == -1 || anchor.Y == room.Height) && anchor.X >= 0 && anchor.X < room.Width);

        public static Issue Validate(RoomDraftModel draft, ValidationStage stage)
        {
            var device = draft.RecoveryDevice;
            if (device == null)
                return new Issue("recoveryDevice.missing", stage == ValidationStage.Publish ? Severity.Error : Severity.Hint,
                    draft.Room.Id, "缺少恢复装置");
            return IsValidAnchor(draft.Room, device.Anchor) ? null :
                new Issue("recoveryDevice.anchor", Severity.Error, device.Id, "恢复装置必须紧邻房间边缘的墙外格");
        }
    }
}