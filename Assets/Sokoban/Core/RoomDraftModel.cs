/**
 * @file RoomDraftModel.cs
 * @brief 编辑中的房间草稿模型：允许缺出生点、缺主入口、没有恢复装置等中间态，持有房间、门、恢复装置以及外观之外的全部可保存数据。
 * RoomDraftModel(room, doors, primaryEntranceId, extras)：用房间建立草稿，门、主入口与透传字段可省略。
 * Room、PrimaryEntranceId、Doors：房间本体、主入口门 ID 与门列表。
 * Meta：房间显示名与主题，赋空时回到空名称，显示名与 ID 分开保存。
 * Decorations、Assets、Graphs、Templates、Instances、ObjectStates、UiImageAssetId：墙外装饰、资源描述符、行为图、对象模板（键为模板 ID）、模板实例、对象状态表（键为对象 ID）与界面图资源 ID。
 * Extras：string 到 string 的透传字段包，Core 不解释，默认为空。
 * ReplaceDoor(index, door)：原位更新门记录，保留门表顺序。
 * RecoveryDevice：当前恢复装置，空值表示尚未放置。
 * ReplaceRecoveryDevice(device)：同步更新装置、ID 和墙外实体占地，冲突时保留旧装置。
 * Register(id, kind)：内部使用，ID 非空且尚未登记时登记到 ID 空间，已存在时静默跳过，供复制与平移草稿时在带错误的草稿上也不抛异常。
 */
using System;
using System.Collections.Generic;

namespace Sokoban.Core
{
    public sealed class RoomDraftModel
    {
        readonly List<DoorInfo> doors = new List<DoorInfo>();
        RoomMeta meta = new RoomMeta("", null);

        public Room Room { get; }
        public string PrimaryEntranceId { get; set; }
        public RoomMeta Meta { get => meta; set => meta = value ?? new RoomMeta("", null); }
        public RecoveryDevice RecoveryDevice { get; private set; }
        public IReadOnlyList<DoorInfo> Doors => doors;
        public Dictionary<string, string> Extras { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<Decoration> Decorations { get; } = new List<Decoration>();
        public List<AssetDescriptor> Assets { get; } = new List<AssetDescriptor>();
        public List<BehaviorGraph> Graphs { get; } = new List<BehaviorGraph>();
        public Dictionary<string, ObjectTemplate> Templates { get; } = new Dictionary<string, ObjectTemplate>(StringComparer.Ordinal);
        public List<TemplateInstance> Instances { get; } = new List<TemplateInstance>();
        public Dictionary<string, ObjectStateTable> ObjectStates { get; } = new Dictionary<string, ObjectStateTable>(StringComparer.Ordinal);
        public string UiImageAssetId { get; set; }

        public RoomDraftModel(Room room, IEnumerable<DoorInfo> doors = null, string primaryEntranceId = null,
            IEnumerable<KeyValuePair<string, string>> extras = null)
        {
            Room = room;
            if (doors != null) this.doors.AddRange(doors);
            PrimaryEntranceId = primaryEntranceId;
            if (extras != null)
                foreach (var pair in extras) Extras[pair.Key] = pair.Value;
        }

        internal void ReplaceRecoveryDevice(RecoveryDevice device)
        {
            if (device != null && RecoveryDeviceRules.HasAnchorCollision(this, device.Anchor))
                throw new InvalidOperationException("恢复装置锚点与现有内容重叠：" + device.Anchor);
            if (device?.Id != RecoveryDevice?.Id)
            {
                if (device != null) Room.Ids.Add(device.Id, "recoveryDevice");
                if (RecoveryDevice != null) Room.Ids.Remove(RecoveryDevice.Id);
            }
            RecoveryDevice = device;
            Room.RecoveryDeviceFloor = device?.Anchor;
        }


        internal void ReplaceDoor(int index, DoorInfo door) => doors[index] = door;

        internal void Register(string id, string kind)
        {
            if (!string.IsNullOrEmpty(id) && !Room.Ids.Contains(id)) Room.Ids.Add(id, kind);
        }
    }
}
