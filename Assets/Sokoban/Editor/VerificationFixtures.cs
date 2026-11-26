/**
 * @file VerificationFixtures.cs
 * @brief 验证脚本共用夹具：默认房间是没有恢复道具与目标的空白房，过不了发布级校验，需要可试玩的房间时由这里补齐。
 * VerificationFixtures.EnsureRecovery(draft)：房间没有恢复道具时，用 AddRecoveryItemCommand 经 draft.Commit 添加一个默认时长的 warehouse-lift 道具，已有则不动。
 */
using Sokoban.Core;

namespace Sokoban.Editor
{
    static class VerificationFixtures
    {
        public static void EnsureRecovery(RoomDraft draft)
        {
            if (draft.Model.RecoveryItems.Count > 0) return;
            draft.Commit(new AddRecoveryItemCommand(draft.Model, new RecoveryItem("recovery-qa", "warehouse-lift", RecoveryItem.DefaultDuration)));
        }
    }
}
