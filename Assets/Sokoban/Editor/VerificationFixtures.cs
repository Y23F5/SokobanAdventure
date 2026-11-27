/**
 * @file VerificationFixtures.cs
 * @brief 验证脚本共用夹具：为缺少恢复装置的测试房间补齐发布级校验要求。
 * VerificationFixtures.EnsureRecovery(draft)：房间没有恢复装置时，在西侧放置默认时长的叉车。
 */
using Sokoban.Core;

namespace Sokoban.Editor
{
    static class VerificationFixtures
    {
        public static void EnsureRecovery(RoomDraft draft)
        {
            if (draft.Model.RecoveryDevice != null) return;
            draft.PlaceRecoveryDevice(new RecoveryDevice("recovery-qa", RecoveryDeviceKind.Forklift,
                new Cell(-1, draft.Room.Height / 2), 1, "warehouse-lift", RecoveryDevice.DefaultDuration));
        }
    }
}
