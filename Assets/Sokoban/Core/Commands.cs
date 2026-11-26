/**
 * @file Commands.cs
 * @brief 命令加反向命令的编辑历史，Dirty 以修订号与保存基线比较。
 * ICommand：一条可撤销的编辑命令，Apply 执行，Revert 为反向命令。
 * CommandHistory.Execute(command)：执行命令并压栈，清空重做栈，修订号加一；命令抛异常时不压栈、不改修订号。
 * CommandHistory.Undo()、Redo()：严格后进先出地撤销与重做，修订号随栈深度变化，回到保存点时 Dirty 为 false。
 * CommandHistory.MarkSaved()：把当前修订号记为保存基线。
 * CommandHistory.Dirty：当前修订号不等于保存基线。
 * PaintStroke：批量笔划命令，先整体预检，任何一格非法则整笔拒绝、不修改房间；Revert 按记录的旧地形逐格还原。
 */
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sokoban.Core
{
    public interface ICommand
    {
        void Apply();
        void Revert();
    }

    public sealed class CommandHistory
    {
        readonly Stack<ICommand> undo = new Stack<ICommand>();
        readonly Stack<ICommand> redo = new Stack<ICommand>();
        int revision;
        int savedRevision;

        public int Revision => revision;
        public bool Dirty => revision != savedRevision;
        public bool CanUndo => undo.Count > 0;
        public bool CanRedo => redo.Count > 0;

        public void Execute(ICommand command)
        {
            command.Apply();
            undo.Push(command);
            redo.Clear();
            revision++;
            if (savedRevision >= revision) savedRevision = -1;
        }

        public bool Undo()
        {
            if (undo.Count == 0) return false;
            var command = undo.Peek();
            command.Revert();
            undo.Pop();
            redo.Push(command);
            revision--;
            return true;
        }

        public bool Redo()
        {
            if (redo.Count == 0) return false;
            var command = redo.Peek();
            command.Apply();
            redo.Pop();
            undo.Push(command);
            revision++;
            return true;
        }

        public void MarkSaved() => savedRevision = revision;
    }

    public sealed class PaintStroke : ICommand
    {
        readonly Room room;
        readonly List<Cell> cells;
        readonly string categoryId;
        readonly List<TerrainCell> previous = new List<TerrainCell>();

        public PaintStroke(Room room, IEnumerable<Cell> cells, string categoryId)
        {
            this.room = room;
            this.cells = cells.Distinct().ToList();
            this.categoryId = categoryId;
        }

        public string Precheck()
        {
            foreach (var cell in cells)
            {
                if (!room.Inside(cell)) return $"格子 {cell} 超出房间，请进入单独编辑后再扩建";
                var current = room.TerrainAt(cell);
                if (categoryId != TerrainCategories.Floor && room.ObjectAt(cell) != null)
                    return $"格子 {cell} 上有对象，无法改为 {categoryId}";
            }
            return null;
        }

        public void Apply()
        {
            var error = Precheck();
            if (error != null) throw new InvalidOperationException(error);
            previous.Clear();
            foreach (var cell in cells) previous.Add(room.TerrainAt(cell));
            foreach (var cell in cells) room.SetTerrain(cell, categoryId);
        }

        public void Revert()
        {
            for (int i = 0; i < cells.Count; i++)
            {
                var old = previous[i];
                room.SetTerrain(cells[i], old.CategoryId, old.EntityId, old.State, old.Rotation);
            }
        }
    }
}
