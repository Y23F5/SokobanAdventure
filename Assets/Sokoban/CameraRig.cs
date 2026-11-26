/**
 * @file CameraRig.cs
 * @brief 游戏与运行编辑器共用的正交镜头姿态及近侧墙裁切。
 * Place 按焦点、偏航、距离和横向偏移布置镜头；ClipWalls 按四档偏航隐藏近侧边界墙。
 */
using UnityEngine;

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

        internal static void ClipWalls(Transform group, int width, int height, int side)
        {
            Vector2Int near = -PuzzleRun.ToVector(Core.Cell.SearchOrder[side]);
            int edgeX = near.x < 0 ? 0 : width - 1;
            int edgeY = near.y < 0 ? 0 : height - 1;
            foreach (Transform part in group)
            {
                if (!part.name.StartsWith("Wall ", System.StringComparison.Ordinal)) continue;
                var position = part.localPosition;
                bool hide = near.x != 0 ? Mathf.RoundToInt(position.x) == edgeX : Mathf.RoundToInt(position.z) == edgeY;
                if (part.gameObject.activeSelf == hide) part.gameObject.SetActive(!hide);
            }
        }
    }
}
