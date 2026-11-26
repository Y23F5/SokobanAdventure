/**
 * @file TransformCommands.cs
 * @brief 旋转放置物和调整房间素材高度的可撤销命令。
 * RotationSteps.Validate(rotation)、Turn(rotation, quarterTurns)、DirectionIndex(direction)：校验档位、循环转动档位、查找四向向量的档位。
 * RotateCommand(holder, cell, quarterTurns)：按格旋转地形或门。按 ID 的重载旋转对象、装饰或门。
 * RotateCommand.Apply/Revert：每次从当前草稿定位，门只更新 Outward，保留格和 ID。非整圈旋转相反方向即可撤销。
 * SetAssetHeightCommand(holder, assetId, heightOffset)：按素材 ID 在当前草稿更新千分之一格高度，撤销恢复原描述符。
 */
using System;

namespace Sokoban.Core
{
    internal static class RotationSteps
    {
        public static int Validate(int rotation)
        {
            if (rotation < 0 || rotation > 3)
                throw new ArgumentOutOfRangeException(nameof(rotation), "旋转档位必须为 0 到 3");
            return rotation;
        }

        public static int Turn(int rotation, int quarterTurns) => (rotation + quarterTurns % 4 + 4) % 4;

        public static int DirectionIndex(Cell direction) => Array.IndexOf(Cell.SearchOrder, direction);
    }

    public sealed class RotateCommand : ICommand
    {
        readonly DraftHolder holder;
        readonly Cell? cell;
        readonly string id;
        readonly int turns;

        public RotateCommand(DraftHolder holder, Cell cell, int quarterTurns)
        {
            this.holder = holder ?? throw new ArgumentNullException(nameof(holder));
            this.cell = cell;
            turns = quarterTurns % 4;
        }

        public RotateCommand(DraftHolder holder, string id, int quarterTurns)
        {
            this.holder = holder ?? throw new ArgumentNullException(nameof(holder));
            this.id = string.IsNullOrEmpty(id) ? throw new ArgumentException("旋转目标 ID 为空", nameof(id)) : id;
            turns = quarterTurns % 4;
        }

        public void Apply() => Rotate(turns);

        public void Revert() => Rotate(-turns);

        void Rotate(int quarterTurns)
        {
            if (quarterTurns == 0) throw new InvalidOperationException("旋转角度未改变");
            var draft = holder.Draft;
            for (int i = 0; i < draft.Doors.Count; i++)
            {
                var door = draft.Doors[i];
                if (cell.HasValue ? door.Cell != cell.Value : door.Id != id) continue;
                int direction = RotationSteps.DirectionIndex(door.Outward);
                if (direction < 0) throw new InvalidOperationException("门朝向不可通行");
                var outward = Cell.SearchOrder[RotationSteps.Turn(direction, quarterTurns)];
                draft.ReplaceDoor(i, new DoorInfo(door.Id, door.Cell, outward, door.Role));
                return;
            }

            if (cell.HasValue)
            {
                var target = cell.Value;
                var terrain = draft.Room.TerrainAt(target);
                if (terrain.CategoryId == TerrainCategories.Void || terrain.CategoryId == TerrainCategories.Door)
                    throw new InvalidOperationException("找不到可旋转的地形或门：" + target);
                draft.Room.SetTerrain(target, terrain.CategoryId, terrain.EntityId, terrain.State,
                    RotationSteps.Turn(terrain.Rotation, quarterTurns));
                return;
            }

            if (draft.Room.RotateObject(id, quarterTurns)) return;
            int index = draft.Decorations.FindIndex(decoration => decoration.Id == id);
            if (index < 0) throw new InvalidOperationException("找不到旋转目标：" + id);
            var old = draft.Decorations[index];
            draft.Decorations[index] = new Decoration(old.Id, old.Cell, old.Appearance,
                RotationSteps.Turn(old.Rotation, quarterTurns));
        }
    }

    public sealed class SetAssetHeightCommand : ICommand
    {
        readonly DraftHolder holder;
        readonly string assetId;
        readonly int milligrid;
        AssetDescriptor before;

        public SetAssetHeightCommand(DraftHolder holder, string assetId, float heightOffset)
        {
            this.holder = holder ?? throw new ArgumentNullException(nameof(holder));
            this.assetId = assetId;
            milligrid = AssetDescriptor.ToMilligrid(heightOffset);
        }

        public void Apply()
        {
            var assets = holder.Draft.Assets;
            int index = FindAsset();
            var asset = assets[index];
            if (asset.HeightMilligrid == milligrid) throw new InvalidOperationException("素材高度偏移未改变");
            before = asset;
            assets[index] = asset.WithHeightMilligrid(milligrid);
        }

        public void Revert() => holder.Draft.Assets[FindAsset()] = before;

        int FindAsset()
        {
            int index = holder.Draft.Assets.FindIndex(asset => asset.Id == assetId);
            if (index < 0) throw new InvalidOperationException("找不到素材：" + assetId);
            return index;
        }
    }
}
