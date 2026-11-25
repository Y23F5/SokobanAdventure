/**
 * @file GridFootprint.cs
 * @brief Measure projects visible mesh vertices into the cell coordinate system. Skinned meshes use their deformed pose.
 * TryMeasure skips hidden branches and degenerate frames. HasUnitSize checks dimensions; Matches also checks the anchor. Describe reports the measured footprint in grid units.
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
            if (!TryMeasure(out var bounds))
                throw new InvalidOperationException("素材缺少可测量的水平宽度或深度");
            return bounds;
        }

        public bool TryMeasure(out Bounds bounds)
        {
            bounds = new Bounds();
            bool started = false;
            foreach (var filter in solids)
                if (VisibleBranch(filter.transform) && filter.sharedMesh && filter.TryGetComponent<MeshRenderer>(out var renderer) && renderer.enabled)
                    Include(filter.sharedMesh, filter.transform, ref bounds, ref started);
            foreach (var skin in skins)
            {
                if (!VisibleBranch(skin.transform) || !skin.enabled || !skin.sharedMesh) continue;
                if (!baked) baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                skin.BakeMesh(baked, true);
                Include(baked, skin.transform, ref bounds, ref started);
            }
            return started && bounds.size.x > .000001f && bounds.size.z > .000001f;
        }

        bool VisibleBranch(Transform part)
        {
            for (var current = part; current != basis; current = current.parent)
                if (!current.gameObject.activeSelf) return false;
            return true;
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

        public static bool HasUnitSize(Bounds bounds) => Mathf.Abs(bounds.size.x - 1) <= Tolerance && Mathf.Abs(bounds.size.z - 1) <= Tolerance;
        public static bool Matches(Bounds bounds) => HasUnitSize(bounds) && Mathf.Abs(bounds.center.x) <= Tolerance && Mathf.Abs(bounds.center.z) <= Tolerance;
        public static string Describe(Bounds bounds) => $"{bounds.size.x:0.####} × {bounds.size.z:0.####} 格";
        public void Dispose()
        {
            if (!baked) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(baked); else UnityEngine.Object.DestroyImmediate(baked);
        }
    }
}
