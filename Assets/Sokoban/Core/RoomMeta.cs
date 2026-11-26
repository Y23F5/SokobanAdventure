/**
 * @file RoomMeta.cs
 * @brief 房间名称与主题、墙外装饰，以及通过发布级校验后生成的只读房间视图。
 * RoomMeta(name, themeId)：房间显示名与主题 ID，显示名与 ID 分开保存，主题只影响外观与推荐。
 * Decoration(id, cell, appearance)：墙外装饰，坐标允许超出房间矩形，不占房内格，不参与任何规则。
 * ValidatedRoom：发布级校验通过后生成的不可变视图，出生点、主入口、入口缓冲格、恢复道具均为非空，规则层运行时只接收它。
 * ValidatedRoom.From(draft, meta)：先对草稿跑发布级校验，存在错误时抛 InvalidOperationException，消息逐条列出规则 ID 与定位；通过后取唯一出生点、主入口门、入口缓冲格（门格减外侧偏移）、门列表与恢复道具生成视图，meta 为空时取草稿自带的 Meta。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class RoomMeta
    {
        public string Name { get; }
        public string ThemeId { get; }

        public RoomMeta(string name, string themeId)
        {
            Name = name;
            ThemeId = themeId;
        }
    }

    public sealed class Decoration
    {
        public string Id { get; }
        public Cell Cell { get; }
        public AppearanceRef Appearance { get; }

        public Decoration(string id, Cell cell, AppearanceRef appearance)
        {
            Id = id;
            Cell = cell;
            Appearance = appearance;
        }
    }

    public sealed class ValidatedRoom
    {
        public Room Room { get; }
        public RoomMeta Meta { get; }
        public PlacedObject Spawn { get; }
        public DoorInfo PrimaryEntrance { get; }
        public Cell EntryBuffer { get; }
        public IReadOnlyList<DoorInfo> Doors { get; }
        public IReadOnlyList<RecoveryItem> RecoveryItems { get; }

        public ValidatedRoom(Room room, RoomMeta meta, PlacedObject spawn, DoorInfo primaryEntrance, Cell entryBuffer,
            IReadOnlyList<DoorInfo> doors, IReadOnlyList<RecoveryItem> recoveryItems)
        {
            Room = room;
            Meta = meta;
            Spawn = spawn;
            PrimaryEntrance = primaryEntrance;
            EntryBuffer = entryBuffer;
            Doors = doors;
            RecoveryItems = recoveryItems;
        }

        public static ValidatedRoom From(RoomDraftModel draft, RoomMeta meta)
        {
            var errors = RoomValidator.ValidateDraft(draft, ValidationStage.Publish).Where(i => i.Severity == Severity.Error).ToList();
            if (errors.Count > 0)
                throw new InvalidOperationException("发布级校验未通过：" + string.Join("；", errors.Select(i => i.RuleId + " @ " + i.Location + "：" + i.Message)));
            var door = draft.Doors.First(d => d.Id == draft.PrimaryEntranceId && d.Role == "entry");
            var spawn = draft.Room.ObjectsOf(ObjectCategories.Spawn).Single();
            return new ValidatedRoom(draft.Room, meta ?? draft.Meta, spawn, door, door.Cell - door.Outward, draft.Doors.ToList(), draft.RecoveryItems.ToList());
        }
    }
}
