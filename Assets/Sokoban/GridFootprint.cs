/**
 * @file GridFootprint.cs
 * @brief Measure projects visible mesh vertices into the cell coordinate system. Skinned meshes use their deformed pose.
 * Matches checks both dimensions and the grid anchor. Describe reports the measured footprint in grid units.
 */
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sokoban
{
    public sealed class GridFootprint : IDisposable
    {
        public const float Tolerance = .001f;
        readonly MeshFilter[] solids;
        readonly SkinnedMeshRenderer[] skins;
        readonly List<Vector3> vertices = new List<Vector3>();
        readonly Transform basis;
        Mesh baked;

        public GridFootprint(Transform basis)
        {
            this.basis = basis;
            solids = basis.GetComponentsInChildren<MeshFilter>(true);
            skins = basis.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        }

        public Bounds Measure()
        {
            var bounds = new Bounds();
            bool started = false;
            foreach (var filter in solids)
                if (filter.sharedMesh && filter.TryGetComponent<MeshRenderer>(out var renderer) && renderer.enabled)
                    Include(filter.sharedMesh, filter.transform, ref bounds, ref started);
            foreach (var skin in skins)
            {
                if (!skin.enabled || !skin.sharedMesh) continue;
                if (!baked) baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                skin.BakeMesh(baked, true);
                Include(baked, skin.transform, ref bounds, ref started);
            }
            if (!started || !(bounds.size.x > .000001f && bounds.size.z > .000001f))
                throw new InvalidOperationException("素材缺少可测量的水平宽度或深度");
            return bounds;
        }

        void Include(Mesh mesh, Transform part, ref Bounds bounds, ref bool started)
        {
            
            /** @details Compose only local transforms to retain precision in distant rooms and the offscreen thumbnail stage. */
            var matrix = Matrix4x4.identity;
            for (var t = part; t != basis; t = t.parent)
                matrix = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * matrix;
            mesh.GetVertices(vertices);
            foreach (var vertex in vertices)
            {
                var point = matrix.MultiplyPoint3x4(vertex);
                if (started) bounds.Encapsulate(point);
                else { bounds = new Bounds(point, Vector3.zero); started = true; }
            }
        }

        public static bool Matches(Bounds bounds) => Mathf.Abs(bounds.size.x - 1) <= Tolerance && Mathf.Abs(bounds.size.z - 1) <= Tolerance
            && Mathf.Abs(bounds.center.x) <= Tolerance && Mathf.Abs(bounds.center.z) <= Tolerance;
        public static string Describe(Bounds bounds) => $"{bounds.size.x:0.###} × {bounds.size.z:0.###} 格";
        public void Dispose()
        {
            if (!baked) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(baked); else UnityEngine.Object.DestroyImmediate(baked);
        }
    }
}
