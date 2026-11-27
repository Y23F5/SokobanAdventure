/**
 * @file EraseDraft.cs
 * @brief 擦除工具先清除格上的箱子、目标和门，格上没有这些内容时才擦除地形。
 * EraseContent(cells)：收集所选格上的非出生点对象与门，合并为一次可撤销提交；没有可清除内容时返回 false，由调用方继续擦除地形。
 * RemoveDoorCommand：复制草稿并移除指定门，门格改回空格，主入口被移除时改选剩余入口，撤销恢复原草稿。
 */
using System;
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;

namespace Sokoban
{
    public sealed partial class RoomDraft
    {
        public bool EraseContent(IEnumerable<Cell> cells)
        {
            var points = cells.Distinct().ToArray();
            var objectIds = new List<string>();
            var doorIds = new List<string>();
            foreach (var cell in points)
            {
                var placed = Room.ObjectAt(cell);
                if (placed != null && placed.CategoryId != ObjectCategories.Spawn) objectIds.Add(placed.Id);
                var door = Model.Doors.FirstOrDefault(entry => entry.Cell == cell);
                if (door != null) doorIds.Add(door.Id);
            }
            if (objectIds.Count + doorIds.Count == 0) return false;
            var holder = holders[ActiveId];
            var commands = new List<ICommand>();
            foreach (string id in objectIds) commands.Add(new RemoveObjectCommand(Model, id));
            foreach (string id in doorIds) commands.Add(new RemoveDoorCommand(holder, id));
            Commit(new DelegateCommand(
                () =>
                {
                    int applied = 0;
                    try
                    {
                        for (; applied < commands.Count; applied++) commands[applied].Apply();
                    }
                    catch
                    {
                        while (applied > 0) commands[--applied].Revert();
                        throw;
                    }
                },
                () =>
                {
                    for (int i = commands.Count - 1; i >= 0; i--) commands[i].Revert();
                }), null);
            return true;
        }

        sealed class RemoveDoorCommand : ICommand
        {
            readonly DraftHolder holder;
            readonly string id;
            RoomDraftModel before, after;

            public RemoveDoorCommand(DraftHolder holder, string id)
            {
                this.holder = holder;
                this.id = id;
            }

            public void Apply()
            {
                before = holder.Draft;
                if (after == null)
                {
                    var door = before.Doors.FirstOrDefault(entry => entry.Id == id);
                    if (door == null) throw new InvalidOperationException("找不到门：" + id);
                    var rest = before.Doors.Where(entry => entry.Id != id).ToList();
                    string primary = before.PrimaryEntranceId == id
                        ? rest.FirstOrDefault(entry => entry.Role == "entry")?.Id
                        : before.PrimaryEntranceId;
                    after = CloneWith(before, rest, primary);
                    after.Room.SetTerrain(door.Cell, TerrainCategories.Void);
                }
                holder.Draft = after;
            }

            public void Revert() => holder.Draft = before;
        }
    }
}
