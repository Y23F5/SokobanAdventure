/**
 * @file RecoveryMethod.cs
 * @brief 恢复方法的宿主接口：Pose(t, ctx) 是纯函数，返回此刻全部零件的完整描述，表现层只做零件池加姿态应用。
 * RecoveryPart：一个零件（零件 ID、资源引用、位置、偏航、俯仰、翻滚、统一缩放与分轴缩放、颜色、可见度、可选的动画剪辑名与采样时刻、可选的预制件子节点名）。
 * RecoveryContext(roomId, boxId, route, duration, parameters, device)：记录房间、任务、路线、时长、数值参数和启动该演出的装置，未提供参数时用空表。
 * IRecoveryMethod.Pose(t, ctx)：t 取 0 到 1，只由进度与上下文决定姿态，不依赖前一帧。
 * RecoveryContext.SeedOf(roomId, objectId)：由房间 ID 加对象 ID 算出稳定种子（FNV-1a），读档重放结果一致。
 * RecoveryPoseCheck.Validate(parts, zone, isRoomPart)：房内零件的格位置必须落在占用区内，越界返回错误文字，房外零件不受限。
 * SlideMethod：默认方法，箱子沿路线格按进度逐格移动，作为脚本方法未提供时的兜底。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public sealed class RecoveryPart
    {
        public string PartId { get; }
        public string Resource { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float Yaw { get; }
        public float Scale { get; }
        public string Color { get; }
        public bool Visible { get; }
        public float? ClipTime { get; }
        public float Pitch { get; }
        public float Roll { get; }
        public float ScaleX { get; }
        public float ScaleY { get; }
        public float ScaleZ { get; }
        public string Clip { get; }
        public string Child { get; }

        public RecoveryPart(string partId, string resource, float x, float y, float z,
            float yaw = 0, float scale = 1, string color = null, bool visible = true, float? clipTime = null,
            float pitch = 0, float roll = 0, float scaleX = 1, float scaleY = 1, float scaleZ = 1,
            string clip = null, string child = null)
        {
            PartId = partId;
            Resource = resource;
            X = x; Y = y; Z = z;
            Yaw = yaw;
            Scale = scale;
            Color = color;
            Visible = visible;
            ClipTime = clipTime;
            Pitch = pitch;
            Roll = roll;
            ScaleX = scaleX;
            ScaleY = scaleY;
            ScaleZ = scaleZ;
            Clip = clip;
            Child = child;
        }
    }

    public sealed class RecoveryContext
    {
        public string RoomId { get; }
        public string BoxId { get; }
        public IReadOnlyList<Cell> Route { get; }
        public float Duration { get; }
        public uint Seed { get; }
        public IReadOnlyDictionary<string, float> Params { get; }
        public RecoveryDevice Device { get; }

        public RecoveryContext(string roomId, string boxId, IReadOnlyList<Cell> route, float duration,
            IReadOnlyDictionary<string, float> parameters = null, RecoveryDevice device = null)
        {
            RoomId = roomId;
            BoxId = boxId;
            Route = route;
            Duration = duration;
            Params = parameters ?? new Dictionary<string, float>();
            Device = device;
            Seed = SeedOf(roomId, boxId);
        }

        public static uint SeedOf(string roomId, string objectId)
        {
            uint hash = 2166136261;
            foreach (char c in roomId + "/" + objectId)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }

    public interface IRecoveryMethod
    {
        IReadOnlyList<RecoveryPart> Pose(float t, RecoveryContext ctx);
    }

    public static class RecoveryPoseCheck
    {
        public static string Validate(IEnumerable<RecoveryPart> parts, ISet<Cell> zone, Func<RecoveryPart, bool> isRoomPart)
        {
            foreach (var part in parts)
            {
                if (!isRoomPart(part)) continue;
                var cell = new Cell((int)Math.Round(part.X), (int)Math.Round(part.Z));
                if (!zone.Contains(cell)) return $"零件 {part.PartId} 位于 {cell}，超出占用区";
            }
            return null;
        }
    }

    public sealed class SlideMethod : IRecoveryMethod
    {
        public IReadOnlyList<RecoveryPart> Pose(float t, RecoveryContext ctx)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            var route = ctx.Route;
            float pos = t * (route.Count - 1);
            int i = Math.Min((int)pos, route.Count - 1);
            int j = Math.Min(i + 1, route.Count - 1);
            float f = pos - i;
            float x = route[i].X + (route[j].X - route[i].X) * f;
            float z = route[i].Y + (route[j].Y - route[i].Y) * f;
            return new[] { new RecoveryPart(ctx.BoxId, "box", x, 0, z) };
        }
    }
}
