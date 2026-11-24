/**
 * @file DioramaLighting.cs
 * @brief Bind applies the shared sky and soft key light to a camera. Apply blends the theme color. Dispose restores the lighting active before this view opened.
 */
using System;
using System.Linq;
using UnityEngine;

namespace Sokoban
{
    internal sealed class DioramaLighting : IDisposable
    {
        internal const float Pitch = 55, Yaw = 32;
        readonly Camera camera;
        readonly Light sun;
        readonly Material sky, originalSky;
        readonly CameraClearFlags flags;
        readonly Color background, originalSun;
        readonly LightShadows shadows;
        readonly float strength;
        internal DioramaLighting(Camera camera)
        {
            this.camera = camera;
            flags = camera.clearFlags;
            background = camera.backgroundColor;
            originalSky = RenderSettings.skybox;
            sky = new Material(Resources.Load<Shader>("DioramaSky"));
            RenderSettings.skybox = sky;
            camera.clearFlags = CameraClearFlags.Skybox;
            sun = UnityEngine.Object.FindObjectsOfType<Light>().FirstOrDefault(light => light.type == LightType.Directional);
            if (!sun) return;
            originalSun = sun.color;
            shadows = sun.shadows;
            strength = sun.shadowStrength;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .55f;
        }
        internal static Color Theme(string id) => id == "forest" ? new Color32(137,192,164,255) : id == "factory" ? new Color32(140,167,184,255) : new Color32(218,197,156,255);
        internal void Apply(Color color, float blend)
        {
            camera.backgroundColor = Color.Lerp(camera.backgroundColor,color,blend);
            sky.SetColor("_Tint",camera.backgroundColor);
            if (sun) sun.color = Color.Lerp(sun.color,Color.Lerp(Color.white,color,.28f),blend);
        }
        public void Dispose()
        {
            if (RenderSettings.skybox == sky)
            {
                RenderSettings.skybox = originalSky;
                if (camera) { camera.clearFlags = flags; camera.backgroundColor = background; }
                if (sun) { sun.color = originalSun; sun.shadows = shadows; sun.shadowStrength = strength; }
            }
            if (sky) UnityEngine.Object.Destroy(sky);
        }
    }
}
