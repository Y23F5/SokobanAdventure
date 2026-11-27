/**
 * @file RoomFile.cs
 * @brief 房间文件的薄工具：全部解析与序列化交给 Core 的 RoomCodec，本文件不再含任何 JObject 代码。
 * Load(path)：经 RoomCodec.Decode 读取房间，读取并检验配套的自定义恢复脚本，格式错误转为 InvalidDataException。
 * Save(path, draft)：先导出当前恢复脚本，再原子写入经 RoomCodec.Decode 验证的房间 JSON。
 * Issues(draft, stage)：转调 RoomValidator.ValidateDraft，两档校验共用 Core 的同一份规则。
 * Check(condition, message)：条件不成立时抛 InvalidDataException。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sokoban.Core;

namespace Sokoban
{
    public static class RoomFile
    {
        public static RoomDraftModel Load(string path)
        {
            try
            {
                var model = RoomCodec.Decode(File.ReadAllText(path));
                RoomAssets.Receive(RecoveryScripts(model), path);
                return model;
            }
            catch (FormatException error) { throw new InvalidDataException(error.Message, error); }
        }

        public static void Save(string path, RoomDraftModel draft)
        {
            string text = RoomCodec.Encode(draft);
            RoomAssets.Publish(RecoveryScripts(draft), path);
            AtomicFile.Write(path, text, temporary => RoomCodec.Decode(File.ReadAllText(temporary)));
        }

        static IEnumerable<AssetDescriptor> RecoveryScripts(RoomDraftModel draft) =>
            draft.Assets.Where(asset => asset.Kind == AssetKinds.Script && asset.Id == draft.RecoveryDevice?.ScriptRef);

        public static List<Issue> Issues(RoomDraftModel draft, ValidationStage stage) => RoomValidator.ValidateDraft(draft, stage);

        public static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
