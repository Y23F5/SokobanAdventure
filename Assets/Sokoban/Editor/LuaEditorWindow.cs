/**
 * @file LuaEditorWindow.cs
 * @brief 可停靠的 Lua 编辑窗口，供行为图中的条件表达式与 Lua 脚本节点展开编辑。
 * Open(title, text, condition, apply)：打开或复用窗口并绑定到一个字段，apply 把编辑结果写回节点参数，写回走行为图窗口的共享撤销历史。
 * CreateGUI：构建等宽字体的多行编辑区、行号栏、Tab 缩进、语法检查结果与写回按钮。
 * Refresh：按当前文本更新行号与语法检查，语法检查调用 BehaviorLua.Check，只编译不执行。
 */
using System;
using System.Linq;
using Sokoban.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Sokoban.Editor
{
    internal sealed class LuaEditorWindow : EditorWindow
    {
        Action<string> apply;
        bool condition;
        bool pose, dirty, conflict;
        string loaded;
        string fingerprint;
        Func<string> poseFingerprint;
        Func<string> readPose, defaultPose;
        string heading = "尚未绑定节点";
        string source = "";
        Label title, gutter, result;
        TextField field;

        internal static void Open(string title, string text, bool condition, Action<string> apply)
        {
            var window = GetWindow<LuaEditorWindow>("Lua 编辑器");
            if (window.pose && window.dirty && !EditorUtility.DisplayDialog("未写回的姿态脚本", "切换编辑内容会丢弃未写回的姿态脚本。", "切换", "取消")) return;
            window.minSize = new Vector2(480, 320);
            window.heading = title;
            window.source = text ?? "";
            window.condition = condition;
            window.pose = false;
            window.readPose = null;
            window.poseFingerprint = null;
            window.defaultPose = null;
            window.apply = apply;
            window.Show();
            window.Focus();
            window.Fill();
        }

        internal static void OpenRecoveryPose(string title, string text, Func<string> fingerprint, Func<string> read, Func<string> defaultSource, Action<string> apply)
        {
            var window = GetWindow<LuaEditorWindow>("Lua 编辑器");
            if (window.pose && window.dirty && !EditorUtility.DisplayDialog("未写回的姿态脚本", "切换编辑内容会丢弃未写回的姿态脚本。", "切换", "取消")) return;
            window.minSize = new Vector2(480, 320);
            window.heading = title;
            window.pose = true;
            window.source = window.loaded = text ?? "";
            window.dirty = window.conflict = false;
            window.readPose = read;
            window.poseFingerprint = fingerprint;
            window.fingerprint = fingerprint();
            window.defaultPose = defaultSource;
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
            field.RegisterValueChangedCallback(evt => { source = evt.newValue; if (pose) dirty = source != loaded; Refresh(); });
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
            bar.Add(new Button(Reload) { text = "重新读取" });
            bar.Add(new Button(DefaultPose) { text = "载入默认姿态" });
            bar.Add(new Button(Commit) { text = "写回" });
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
            string error = Check();
            result.text = conflict ? "装置脚本或绑定已变化；请重新读取后再写回。" : error == null ? "检查通过" : "检查失败：" + error;
        }

        string Check()
        {
            if (!pose) return BehaviorLua.Check(source, condition);
            try { LuaRecoveryMethod.ValidateSource(source); return null; }
            catch (Exception error) { return error.Message; }
        }

        void Update()
        {
            if (!pose || readPose == null || field == null) return;
            try
            {
                string stamp = poseFingerprint?.Invoke();
                if (stamp == null) { if (!conflict) { conflict = true; Refresh(); } return; }
                if (stamp == fingerprint) return;
                fingerprint = stamp;
                string current = readPose();
                if (current == null) { conflict = true; Refresh(); return; }
                if (current == loaded) return;
                if (dirty) { conflict = true; Refresh(); return; }
                source = loaded = current;
                fingerprint = poseFingerprint?.Invoke();
                conflict = false;
                Fill();
            }
            catch (Exception error) { conflict = true; result.text = error.Message; }
        }

        void Reload()
        {
            if (!pose || readPose == null) return;
            if (dirty && !EditorUtility.DisplayDialog("未写回的姿态脚本", "重新读取会丢弃未写回的修改。", "重新读取", "取消")) return;
            try
            {
                string current = readPose();
                if (current == null) throw new InvalidOperationException("装置或房间已切换，请重新打开编辑器");
                source = loaded = current;
                dirty = conflict = false;
                fingerprint = poseFingerprint?.Invoke();
                Fill();
            }
            catch (Exception error) { result.text = error.Message; }
        }

        void DefaultPose()
        {
            if (!pose || defaultPose == null) return;
            if (dirty && !EditorUtility.DisplayDialog("未写回的姿态脚本", "载入默认姿态会替换当前未写回的修改。", "载入", "取消")) return;
            try
            {
                string value = defaultPose();
                if (value == null) throw new InvalidOperationException("装置或房间已切换，请重新打开编辑器");
                source = value;
                dirty = source != loaded;
                Fill();
            }
            catch (Exception error) { result.text = error.Message; }
        }

        void Commit()
        {
            if (apply == null) { result.text = "没有绑定的节点，请从行为图中点击展开编辑。"; return; }
            if (pose && conflict) { result.text = "装置脚本或绑定已变化；请重新读取后再写回。"; return; }
            string error = Check();
            if (pose && error != null) { result.text = "检查失败：" + error; return; }
            try
            {
                if (pose && readPose() != loaded) { conflict = true; Refresh(); return; }
                apply(source);
                if (pose) { loaded = source; dirty = false; fingerprint = poseFingerprint?.Invoke(); }
                Refresh();
            }
            catch (Exception failure) { result.text = failure.Message; }
        }

        void OnLostFocus() { if (!pose) Commit(); }

        internal static void VerifyRecoveryPoseEditor(RoomDraft draft)
        {
            var window = GetWindow<LuaEditorWindow>();
            if (!window.pose || window.field == null) throw new InvalidOperationException("姿态 Lua 编辑器未绑定");
            string original = draft.GetRecoveryPoseSource();
            const string changed = "function pose(t, ctx) return {{id=ctx.boxId, x=1, y=0, z=2}} end";
            window.field.value = "function pose(";
            window.Commit();
            if (draft.GetRecoveryPoseSource() != original) throw new InvalidOperationException("无效脚本被写入");
            window.field.value = changed;
            window.Commit();
            if (draft.GetRecoveryPoseSource() != changed) throw new InvalidOperationException("姿态脚本未写入");
            draft.Undo();
            window.Update();
            if (window.source != original) throw new InvalidOperationException("撤销后姿态编辑器未刷新");
            draft.Redo();
            window.Update();
            if (window.source != changed) throw new InvalidOperationException("重做后姿态编辑器未刷新");
        }
    }
}
