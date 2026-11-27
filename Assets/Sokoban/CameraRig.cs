/**
 * @file CameraRig.cs
 * @brief 游戏与运行编辑器共用的正交镜头姿态及近侧墙裁切。
 * Place 按焦点、偏航、距离和横向偏移布置镜头；ClipWalls 按原型的视角与墙格方向掩码裁切整段墙。
 * ViewMask 将 Unity 镜头的反向水平投影映射到原型四个方向位；WallMask 查询墙后邻格与房间边界。
 */
using UnityEngine;
using Sokoban.Core;

namespace Sokoban
{
    internal static class CameraRig
    {
        internal const float Pitch = 48f;
        internal static int Side(float yaw) => (Mathf.RoundToInt(yaw / 90f) % 4 + 4) % 4;

        internal static void Place(Camera lens, Vector3 target, float yaw, float distance, float lateral = 0)
        {
            lens.transform.rotation = Quaternion.Euler(Pitch, yaw, 0);
            lens.transform.position = target - lens.transform.forward * distance + lens.transform.right * lateral;
        }

        internal static int ViewMask(Quaternion rotation)
        {
            Vector3 near = rotation * Vector3.back;
            var horizontal = new Vector2(near.x, near.z);
            if (horizontal.sqrMagnitude < .0001f) return 0;
            horizontal.Normalize();
            return (horizontal.y > .32f ? 1 : 0) | (horizontal.x > .32f ? 2 : 0)
                | (horizontal.y < -.32f ? 4 : 0) | (horizontal.x < -.32f ? 8 : 0);
        }

        static int WallMask(Room room, Cell cell)
        {
            bool Behind(Cell direction)
            {
                string terrain = room.TerrainAt(cell + direction).CategoryId;
                return terrain != TerrainCategories.Wall && terrain != TerrainCategories.Void;
            }

            return (cell.Y == room.Height - 1 || Behind(Cell.Down) ? 1 : 0)
                | (cell.X == room.Width - 1 || Behind(Cell.Left) ? 2 : 0)
                | (cell.Y == 0 || Behind(Cell.Up) ? 4 : 0)
                | (cell.X == 0 || Behind(Cell.Right) ? 8 : 0);
        }

        internal static void ClipWalls(Transform group, Room room, int viewMask)
        {
            foreach (Transform part in group)
            {
                if (!part.name.StartsWith("Wall ", System.StringComparison.Ordinal)) continue;
                var position = part.localPosition;
                var cell = new Cell(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z));
                bool hide = (WallMask(room, cell) & viewMask) != 0;
                if (part.gameObject.activeSelf == hide) part.gameObject.SetActive(!hide);
            }
        }
    }
}
