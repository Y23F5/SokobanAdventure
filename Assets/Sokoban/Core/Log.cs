/**
 * @file Log.cs
 * @brief 可整体开关的调试日志；未定义编译符号 SOKOBAN_DEBUG 时，所有 Log.Write 调用连同参数求值一并被编译器移除。
 * Log.Write(category, message)：按类别输出一条日志，仅在定义 SOKOBAN_DEBUG 时存在。
 * Log.Enabled：运行时总开关。
 * Log.Mute(category)、Log.Unmute(category)：按类别静音或恢复。
 * Log.Sink：输出目标，默认写控制台，Unity 端可替换为 UnityEngine.Debug.Log。
 */
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Sokoban.Core
{
    public static class Log
    {
        public static bool Enabled = true;
        public static Action<string, string> Sink = (category, message) => Console.WriteLine("[" + category + "] " + message);
        static readonly HashSet<string> muted = new HashSet<string>();

        public static void Mute(string category) { muted.Add(category); }
        public static void Unmute(string category) { muted.Remove(category); }

        [Conditional("SOKOBAN_DEBUG")]
        public static void Write(string category, string message)
        {
            if (Enabled && !muted.Contains(category)) Sink(category, message);
        }
    }
}
