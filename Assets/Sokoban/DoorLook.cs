/**
 * @file DoorLook.cs
 * @brief 门外观配置：说明入口、出口的开关表现用哪一档，随房间 Extras 保存，Core 不解释。
 * DoorLook.Parse(text)：把 Extras 里的配置串读成对象，空串或格式不对时返回默认的整门显隐档。
 * DoorLook.Encode()：写回配置串，字段用分号分隔，键值用等号。
 * RoomDraft.DoorLookOf(doorId)：读取某扇门的配置，没有记录时返回默认值。
 * RoomDraft.SetDoorLook(doorId, look)：经 ModelEdit 提交，可撤销，保存重开后保持。
 */
using System;
using System.Globalization;
using System.Linq;
using Sokoban.Core;

namespace Sokoban
{
    public enum DoorMode { Visibility, Clips, TwoModels }

    public sealed class DoorLook
    {
        public const string Prefix = "doorLook/";
        public DoorMode Mode { get; }
        public string OpenClip { get; }
        public string CloseClip { get; }
        public string OpenModel { get; }
        public string CloseModel { get; }

        public DoorLook(DoorMode mode, string openClip = null, string closeClip = null, string openModel = null, string closeModel = null)
        {
            Mode = mode;
            OpenClip = openClip ?? "open";
            CloseClip = closeClip ?? "close";
            OpenModel = openModel;
            CloseModel = closeModel;
        }

        public static readonly DoorLook Default = new DoorLook(DoorMode.Visibility);

        public static DoorLook Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return Default;
            var fields = text.Split(';').Select(part => part.Split(new[] { '=' }, 2)).Where(pair => pair.Length == 2)
                .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
            string Get(string key) => fields.TryGetValue(key, out var value) && value.Length > 0 ? value : null;
            return Enum.TryParse(Get("mode"), out DoorMode mode)
                ? new DoorLook(mode, Get("openClip"), Get("closeClip"), Get("openModel"), Get("closeModel"))
                : Default;
        }

        public string Encode() => string.Join(";", new[]
        {
            "mode=" + Mode, "openClip=" + OpenClip, "closeClip=" + CloseClip,
            "openModel=" + OpenModel, "closeModel=" + CloseModel
        });

        public string Describe() => Mode == DoorMode.Clips ? "动画：" + OpenClip + " / " + CloseClip
            : Mode == DoorMode.TwoModels ? "开关两个模型" : "整门显示或隐藏";
    }

    public sealed partial class RoomDraft
    {
        public DoorLook DoorLookOf(string doorId) =>
            Model.Extras.TryGetValue(DoorLook.Prefix + doorId, out var text) ? DoorLook.Parse(text) : DoorLook.Default;

        public void SetDoorLook(string doorId, DoorLook look)
        {
            var model = Model;
            RoomFile.Check(model.Doors.Any(door => door.Id == doorId), "请选择门");
            string key = DoorLook.Prefix + doorId;
            bool had = model.Extras.TryGetValue(key, out string before);
            string after = look.Encode();
            if (had && before == after) return;
            Commit(new ModelEdit(() => model.Extras[key] = after, () =>
            {
                if (had) model.Extras[key] = before; else model.Extras.Remove(key);
            }), doorId);
        }
    }
}
