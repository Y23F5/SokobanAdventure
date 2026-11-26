/**
 * @file RoomFile.cs
 * @brief 房间文件的薄工具：全部解析与序列化交给 Core 的 RoomCodec，本文件不再含任何 JObject 代码。
 * Load(path)：读文本并经 RoomCodec.Decode 得到 RoomDraftModel，格式错误统一转为 InvalidDataException。
 * Save(path, draft)：经 RoomCodec.Encode 得到文本，再由 AtomicFile.Write 写同目录临时文件，用 RoomCodec.Decode 试读通过后才替换目标。
 * Issues(draft, stage)：转调 RoomValidator.ValidateDraft，两档校验共用 Core 的同一份规则。
 * Check(condition, message)：条件不成立时抛 InvalidDataException。
 */
using System;
using System.Collections.Generic;
using System.IO;
using Sokoban.Core;

namespace Sokoban
{
    public static class RoomFile
    {
        public static RoomDraftModel Load(string path)
        {
            try { return RoomCodec.Decode(File.ReadAllText(path)); }
            catch (FormatException error) { throw new InvalidDataException(error.Message, error); }
        }

        public static void Save(string path, RoomDraftModel draft)
        {
            string text = RoomCodec.Encode(draft);
            AtomicFile.Write(path, text, temporary => RoomCodec.Decode(File.ReadAllText(temporary)));
        }

        public static List<Issue> Issues(RoomDraftModel draft, ValidationStage stage) => RoomValidator.ValidateDraft(draft, stage);

        public static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidDataException(message);
        }
    }
}
