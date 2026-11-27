/**
 * @file RecoveryDeviceDraft.cs
 * @brief 两端共用的恢复装置命令入口。
 * FindRecoveryDevice(id)：按 ID 查询当前房间的装置。
 * PlaceRecoveryDevice(device, replaceExisting)：提交放置或显式替换，并选中装置。
 * MoveRecoveryDevice(anchor)：提交移动，并保持选中。
 * RotateRecoveryDevice(quarterTurns)：提交旋转，并保持选中。
 * SetRecoveryDeviceDetails(duration, itemName, itemDescription, itemAcquiredHint)：提交时长与道具文字的编辑。
 * SetRecoveryDeviceParam(key, value)：提交一个参数的编辑。
 * DeleteRecoveryDevice()：提交删除并清除选中。
 */
using Sokoban.Core;

namespace Sokoban
{
    public sealed partial class RoomDraft
    {
        public RecoveryDevice FindRecoveryDevice(string id) =>
            Model.RecoveryDevice?.Id == id ? Model.RecoveryDevice : null;

        public void PlaceRecoveryDevice(RecoveryDevice device, bool replaceExisting = false) =>
            Commit(RecoveryDeviceCommands.Place(holders[ActiveId], device, replaceExisting), device.Id);

        public void MoveRecoveryDevice(Cell anchor) =>
            Commit(RecoveryDeviceCommands.Move(holders[ActiveId], anchor), Model.RecoveryDevice?.Id);

        public void RotateRecoveryDevice(int quarterTurns) =>
            Commit(RecoveryDeviceCommands.Rotate(holders[ActiveId], quarterTurns), Model.RecoveryDevice?.Id);

        public void SetRecoveryDeviceDetails(float duration, string itemName, string itemDescription, string itemAcquiredHint) =>
            Commit(RecoveryDeviceCommands.SetDetails(holders[ActiveId], duration, itemName, itemDescription, itemAcquiredHint),
                Model.RecoveryDevice?.Id);

        public void SetRecoveryDeviceParam(string key, float value) =>
            Commit(RecoveryDeviceCommands.SetParam(holders[ActiveId], key, value), Model.RecoveryDevice?.Id);

        public void DeleteRecoveryDevice() =>
            Commit(RecoveryDeviceCommands.Delete(holders[ActiveId]));
    }
}