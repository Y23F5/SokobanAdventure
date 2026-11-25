/**
 * @file InterfaceTheme.cs
 * @brief Inspector-editable ink, parchment, highlight, typography, spacing and control transition settings shared by uGUI pages.
 * Controls creates the color-tint states while preserving each control's configured image and navigation.
 * OnValidate increments the preview version so open scenes can refresh outside the serialization callback.
 */
using UnityEngine;
using UnityEngine.UI;

namespace Sokoban
{
    [CreateAssetMenu(menuName = "Sokoban/界面主题")]
    public sealed class InterfaceTheme : ScriptableObject
    {
        public Font font;
        public Font titleFont;
        public Color ink = new Color32(84, 55, 43, 255);
        public Color parchment = new Color32(255, 228, 182, 255);
        public Color active = new Color32(255, 204, 115, 255);
        public Color panel = new Color32(255, 239, 205, 255);
        public Color field = new Color32(248, 216, 166, 255);
        public Color hover = new Color32(255, 243, 212, 255);
        public Color pressed = new Color32(224, 190, 152, 255);
        public Color focus = new Color32(250, 210, 138, 255);
        public Color disabled = new Color32(192, 183, 160, 185);
        [Range(-3, 5)] public int fontSizeAdjustment;
        [Range(-4, 8)] public float spacingAdjustment;
        [Range(0, .4f)] public float transitionSeconds = .12f;
        public Sprite controlFrame;
        internal int Revision { get; private set; }

        public ColorBlock Controls => new ColorBlock
        {
            normalColor = Color.white, highlightedColor = hover, pressedColor = pressed,
            selectedColor = focus, disabledColor = disabled, colorMultiplier = 1, fadeDuration = transitionSeconds
        };

        void OnValidate() => Revision++;
    }
}
