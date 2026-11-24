/**
 * @file LayoutText.cs
 * @brief OnEnable 为当前界面的默认文字共用本机中文字体；OnDisable 在界面关闭时释放共享引用，重开时生成新字形图集。
 */
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban
{
    [AddComponentMenu("UI/Sokoban Layout Text")]
    public sealed class LayoutText : Text
    {
        static Font systemFont;
        static int users;
        bool usesSystemFont;
        protected override void OnEnable()
        {
            if (!font || font.name == "LegacyRuntime" || font.name == "Arial")
            {
                if (!systemFont)
                {
                    systemFont = Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei UI","Microsoft YaHei","SimHei","Noto Sans CJK SC","Arial"}, 18);
                    systemFont.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                }
                font = systemFont;
                users++;
                usesSystemFont = true;
            }
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (!usesSystemFont) return;
            usesSystemFont = false;
            if (font == systemFont) font = null;
            if (--users == 0) systemFont = null;
        }
    }
}
