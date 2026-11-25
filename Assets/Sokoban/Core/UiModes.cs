/**
 * @file UiModes.cs
 * @brief 界面模式状态机：布局、检查、试玩、单独编辑四种模式，返回只回到上一模式，输入遮罩由模式统一给出。
 * UiMode：Layout、Check、Trial、StandaloneEdit。
 * UiModeMachine.Current：当前模式。
 * UiModeMachine.Enter(mode)：进入模式并把当前模式压栈；进入当前模式本身时忽略；Trial 不能从 Check 直接进入。
 * UiModeMachine.Back()：弹出上一模式并返回 true；栈空时保持布局模式返回 false。
 * UiModeMachine.BlocksMapInput：非布局与非单独编辑模式（检查、试玩）时为 true，地图不接收输入，替代逐个元素勾选射线接收。
 * UiModeMachine.Changed：模式变化事件，参数为旧模式与新模式。
 */
using System;
using System.Collections.Generic;

namespace Sokoban.Core
{
    public enum UiMode { Layout, Check, Trial, StandaloneEdit }

    public sealed class UiModeMachine
    {
        readonly Stack<UiMode> history = new Stack<UiMode>();

        public UiMode Current { get; private set; } = UiMode.Layout;
        public event Action<UiMode, UiMode> Changed;

        public bool BlocksMapInput => Current == UiMode.Check || Current == UiMode.Trial;

        public bool Enter(UiMode mode)
        {
            if (mode == Current) return false;
            if (mode == UiMode.Trial && Current == UiMode.Check) return false;
            var old = Current;
            history.Push(old);
            Current = mode;
            Changed?.Invoke(old, mode);
            return true;
        }

        public bool Back()
        {
            if (history.Count == 0) return false;
            var old = Current;
            Current = history.Pop();
            Changed?.Invoke(old, Current);
            return true;
        }
    }
}
