/**
 * @file CampaignSlot.cs
 * @brief 正式单槽存档：读档分无档、损坏、不兼容三态，保存走原子写入并先试读。
 * Probe()：装配正式世界，用 SaveCodec.Decode 判定三态，字段校验通过后在候选会话里重建进度，重建失败归为损坏。
 * Load()：读档成功返回会话，其余状态抛带原因的 InvalidOperationException。
 * Available()：槽位可继续时为真，菜单需要区分提示时改用 Probe 与 Describe。
 * Describe(result)：三态对应的菜单提示文字。
 * Save(run)：SaveCodec.Encode 序列化并试读，再经 AtomicFile.Write 替换文件，临时文件读回内容不符或试读失败则保留原文件。
 */
using System;
using System.IO;
using UnityEngine;
using Sokoban.Core;

namespace Sokoban
{
    public sealed class CampaignLoad
    {
        public readonly SaveStatus Status;
        public readonly string Reason;
        public readonly PuzzleRun Run;
        internal CampaignLoad(SaveStatus status, string reason, PuzzleRun run) { Status = status; Reason = reason; Run = run; }
    }

    public static class CampaignSlot
    {
        public static string PathName => Path.Combine(Application.persistentDataPath, "campaign.json");

        public static CampaignLoad Probe()
        {
            var run = PuzzleRun.Campaign();
            string text = File.Exists(PathName) ? File.ReadAllText(PathName) : null;
            var load = SaveCodec.Decode(text, run.WorldId, run.Hashes);
            if (load.Status != SaveStatus.Ok) return new CampaignLoad(load.Status, load.Reason, run);
            try { run.Restore(load.Data); }
            catch (Exception error) when (error is FormatException || error is InvalidOperationException || error is ArgumentException)
            { return new CampaignLoad(SaveStatus.Corrupt, error.Message, run); }
            return new CampaignLoad(SaveStatus.Ok, null, run);
        }

        public static string Describe(CampaignLoad result) => result.Status switch
        {
            SaveStatus.NoSave => "没有可继续的存档",
            SaveStatus.Corrupt => "存档已损坏：" + result.Reason,
            SaveStatus.Incompatible => "存档与当前关卡内容不兼容：" + result.Reason,
            _ => "存档可用"
        };

        public static PuzzleRun Load()
        {
            var result = Probe();
            if (result.Status != SaveStatus.Ok) throw new InvalidOperationException(Describe(result));
            return result.Run;
        }

        public static bool Available()
        {
            try { return Probe().Status == SaveStatus.Ok; }
            catch (Exception error) { Debug.LogWarning("正式进度暂时无法读取：" + error.Message); return false; }
        }

        public static void Save(PuzzleRun run)
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            string contents = SaveCodec.Encode(run.Snapshot());
            AtomicFile.Write(PathName, contents, temp =>
            {
                string back = File.ReadAllText(temp);
                if (back != contents) throw new InvalidDataException("进度写入校验失败");
                var probe = SaveCodec.Decode(back, run.WorldId, run.Hashes);
                if (probe.Status != SaveStatus.Ok) throw new InvalidDataException("进度试读失败：" + probe.Reason);
            });
        }
    }
}
