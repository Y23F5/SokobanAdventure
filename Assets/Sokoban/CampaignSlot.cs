/**
 * @file CampaignSlot.cs
 * @brief Load 按当前世界定义核验单槽；Available 对菜单报告可继续状态。
 * Save 使用统一原子文件写入接口替换已提交的逻辑状态。
 */
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public static class CampaignSlot
    {
        public static string PathName => Path.Combine(Application.persistentDataPath, "campaign.json");

        public static PuzzleRun Load()
        {
            var run = PuzzleRun.Campaign();
            run.Restore(JObject.Parse(File.ReadAllText(PathName)));
            return run;
        }

        public static bool Available()
        {
            if (!File.Exists(PathName)) return false;
            try { Load(); return true; }
            catch (Exception error) { Debug.LogWarning("正式进度暂时无法读取：" + error.Message); return false; }
        }

        public static void Save(PuzzleRun run)
        {
            Directory.CreateDirectory(Application.persistentDataPath);
            string contents = run.Snapshot().ToString();
            RoomFile.Write(PathName, contents, temp => {
                if (File.ReadAllText(temp) != contents)
                    throw new InvalidDataException("进度写入校验失败");
            });
        }
    }
}
