/**
 * @file LuaEditorWindow.cs
 * @brief 可停靠的 Lua 编辑窗口，供行为图中的条件表达式与 Lua 脚本节点展开编辑。
 * Open(title, text, condition, apply)：打开或复用窗口并绑定到一个字段，apply 把编辑结果写回节点参数，写回走行为图窗口的共享撤销历史。
 * CreateGUI：构建等宽字体的多行编辑区、行号栏、Tab 缩进、语法检查结果与写回按钮。
 * Refresh：按当前文本更新行号与语法检查，语法检查调用 BehaviorLua.Check，只编译不执行。
 */
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sokoban.Editor
{
    internal sealed class LuaEditorWindow : EditorWindow
    {
        Action<string> apply;
        bool condition;
        string heading = "尚未绑定节点";
        string source = "";
        Label title, gutter, result;
        TextField field;

        internal static void Open(string title, string text, bool condition, Action<string> apply)
        {
            var window = GetWindow<LuaEditorWindow>("Lua 编辑器");
            window.minSize = new Vector2(480, 320);
            window.heading = title;
            window.source = text ?? "";
            window.condition = condition;
            window.apply = apply;
            window.Show();
            window.Focus();
            window.Fill();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            var mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New" }, 13);
            title = new Label { style = { unityFontStyleAndWeight = FontStyle.Bold, marginLeft = 6, marginTop = 4 } };
            rootVisualElement.Add(title);
            var body = new ScrollView { style = { flexGrow = 1 } };
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            gutter = new Label { style = { unityFont = mono, unityTextAlign = TextAnchor.UpperRight, minWidth = 34, paddingRight = 6, opacity = 0.6f } };
            field = new TextField { multiline = true, style = { flexGrow = 1, minHeight = 240 } };
            var input = field.Q(TextField.textInputUssName);
            input.style.unityFont = mono;
            input.style.whiteSpace = WhiteSpace.NoWrap;
            field.RegisterValueChangedCallback(evt => { source = evt.newValue; Refresh(); });
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode != KeyCode.Tab) return;
                int at = Mathf.Min(field.cursorIndex, field.value.Length);
                field.value = field.value.Insert(at, "  ");
                field.SelectRange(at + 2, at + 2);
                evt.StopPropagation();
                evt.PreventDefault();
            }, TrickleDown.TrickleDown);
            row.Add(gutter);
            row.Add(field);
            body.Add(row);
            rootVisualElement.Add(body);
            result = new Label { style = { whiteSpace = WhiteSpace.Normal, marginLeft = 6, minHeight = 36 } };
            rootVisualElement.Add(result);
            var bar = new VisualElement { style = { flexDirection = FlexDirection.Row, justifyContent = Justify.FlexEnd } };
            bar.Add(new Button(Commit) { text = "写回节点" });
            rootVisualElement.Add(bar);
            Fill();
        }

        void Fill()
        {
            if (field == null) return;
            title.text = heading;
            field.SetValueWithoutNotify(source);
            Refresh();
        }

        void Refresh()
        {
            int lines = source.Count(c => c == '\n') + 1;
            gutter.text = string.Join("\n", Enumerable.Range(1, lines));
            string error = BehaviorLua.Check(source, condition);
            result.text = error == null ? "语法检查通过" : "语法错误：" + error;
        }

        void Commit()
        {
            if (apply == null) { result.text = "没有绑定的节点，请从行为图中点击展开编辑。"; return; }
            apply(source);
        }

        void OnLostFocus() => Commit();
    }
}
