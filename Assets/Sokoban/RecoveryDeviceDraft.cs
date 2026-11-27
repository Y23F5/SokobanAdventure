/**
 * @file RecoveryDeviceDraft.cs
 * @brief 两端共用的恢复装置命令入口。
 * FindRecoveryDevice(id)：按 ID 查询当前房间的装置。
 * RecoveryDeviceGraphs()：读取当前装置拥有的图，缺少装置时返回空列表。
 * RecoveryDeviceGraphTemplate()：按当前装置类型创建尚未提交的默认图。
 * SetRecoveryDeviceGraph(graph)：按图 ID 新增或更新装置图，经共享历史提交。
 * PlaceRecoveryDevice(device, replaceExisting)：提交放置或显式替换，并选中装置。
 * MoveRecoveryDevice(anchor)：提交移动，并保持选中。
 * RotateRecoveryDevice(quarterTurns)：提交旋转，并保持选中。
 * SetRecoveryDeviceDetails(duration, itemName, itemDescription, itemAcquiredHint)：提交时长与道具文字的编辑。
 * SetRecoveryDeviceParam(key, value)：提交一个参数的编辑。
 * DeleteRecoveryDevice()：提交删除并清除选中。
 */
using Sokoban.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban
{
    public sealed partial class RoomDraft
    {
        public RecoveryDevice FindRecoveryDevice(string id) =>
            Model.RecoveryDevice?.Id == id ? Model.RecoveryDevice : null;

        public IReadOnlyList<BehaviorGraph> RecoveryDeviceGraphs() => Model.RecoveryDevice == null
            ? Array.Empty<BehaviorGraph>()
            : Model.Graphs.Where(graph => graph.OwnerId == Model.RecoveryDevice.Id).ToArray();

        public BehaviorGraph RecoveryDeviceGraphTemplate()
        {
            var device = Model.RecoveryDevice ?? throw new InvalidOperationException("缺少恢复装置");
            return RecoveryDeviceCommands.DefaultGraph(device.Kind, device.Id);
        }

        public void SetRecoveryDeviceGraph(BehaviorGraph graph)
        {
            var device = Model.RecoveryDevice ?? throw new InvalidOperationException("缺少恢复装置");
            Commit(RecoveryDeviceCommands.SetGraph(holders[ActiveId], graph), device.Id);
        }

        public void PlaceRecoveryDevice(RecoveryDevice device, bool replaceExisting = false)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));
            CheckRecoveryFloorPlacement(device.Anchor);
            Commit(RecoveryDeviceCommands.Place(holders[ActiveId], device, replaceExisting), device.Id);
        }

        public void MoveRecoveryDevice(Cell anchor)
        {
            CheckRecoveryFloorPlacement(anchor);
            Commit(RecoveryDeviceCommands.Move(holders[ActiveId], anchor), Model.RecoveryDevice?.Id);
        }

        void CheckRecoveryFloorPlacement(Cell anchor)
        {
            string error = WorldLayout.RecoveryDeviceFloorIssue(World, Catalog, ActiveId, anchor);
            if (error != null) throw new InvalidOperationException(error);
        }

        bool RecoveryFloorBlocksDecoration(Cell cell)
        {
            if (Room.IsRecoveryDeviceFloor(cell)) return true;
            if (Placement == null) return false;
            Cell target = Origin + cell;
            return World.Rooms.Any(room => room.LevelId != ActiveId &&
                Catalog.TryGetValue(room.LevelId, out var model) &&
                model.Room.RecoveryDeviceFloor is Cell floor && room.Origin + floor == target);
        }

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
