/**
 * @file WS06EntryVerification.cs
 * @brief 验证反向越界矩形框选。
 * Run()：在空白草稿布置三个地板格，检查框选裁剪越界范围、跳过空地并追加至原有选择。
 */
using System;
using Sokoban.Core;
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class WS06EntryVerification
    {
        [MenuItem("Sokoban/验证/WS-06 矩形框选")]
        public static void Run()
        {
            var draft = new LayoutPanel().Draft;
            draft.NewBlankRoom();
            var points = new[] { new Cell(0, 0), new Cell(1, 1), new Cell(7, 7) };
            if (draft.Brush(points, RoomTool.Floor) != null) throw new InvalidOperationException("空白房铺地板失败");
            draft.Select(points[2]);
            draft.SelectRectangle(new Cell(1, 1), new Cell(-1, -1));
            if (!draft.Selection.SetEquals(points)) throw new InvalidOperationException("反向框选越界裁剪、跳过 Void 或追加选择失败");
            Debug.Log("WS06ENT PASS Rectangle 反向越界框选保留旧格并跳过空地");
        }
    }
}
