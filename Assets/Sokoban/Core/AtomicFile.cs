/**
 * @file AtomicFile.cs
 * @brief 原子文件写入：先写同目录临时文件并试读校验，通过后才替换目标，失败保留原文件且不留临时文件。
 * AtomicFile.Write(path, contents, validateTemporary)：先把内容写到同目录的临时文件，再调用 validateTemporary 读取并校验该临时文件，校验抛异常则原样上抛；
 * 通过后目标不存在时直接移动，已存在时用 File.Replace 替换，遇到文件被占用或锁定（HResult 低 16 位为 32 或 1175）时最多重试 5 次、间隔递增；
 * 无论成败都删除残留的临时文件。
 */
using System;
using System.IO;
using System.Threading;

namespace Sokoban.Core
{
    public static class AtomicFile
    {
        public static void Write(string path, string contents, Action<string> validateTemporary = null)
        {
            path = Path.GetFullPath(path);
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, contents);
                validateTemporary?.Invoke(temp);
                if (!File.Exists(path)) { File.Move(temp, path); return; }
                for (int attempt = 0; ; attempt++)
                {
                    try { File.Replace(temp, path, null); break; }
                    catch (IOException error) when (attempt < 5 && ((error.HResult & 65535) == 1175 || (error.HResult & 65535) == 32))
                    { Thread.Sleep(30 * (attempt + 1)); }
                }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
