/**
 * @file ObjectStateView.cs
 * @brief Renders a configurable object's current state below its stable transform. Create handles the same registered prefabs and room GLB bindings as ordinary placement.
 */
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public sealed class ObjectStateView : MonoBehaviour
    {
        public JToken Profile { get; private set; }
        JObject room;
        JToken item;
        string defaultPrefab;
        GameObject visible;
        readonly Dictionary<string, GameObject> variants = new Dictionary<string, GameObject>();

        internal void Configure(JObject design, JToken record, string fallback)
        {
            room = design;
            item = record;
            defaultPrefab = fallback;
            Profile = record["stateProfile"];
            Show((string)Profile["initial"]);
        }

        internal void Show(string id)
        {
            id ??= (string)Profile["initial"];
            if (!variants.TryGetValue(id, out var variant)) variants.Add(id, variant = CreateVariant(id, transform));
            if (visible == variant) return;
            if (visible) visible.SetActive(false);
            visible = variant;
            visible.SetActive(true);
        }

        internal GameObject CreateVariant(string id, Transform parent, string motion = null)
        {
            var state = Profile["states"][id];
            string prefab = ObjectStateProfile.Prefab((string)state["builtinModel"]);
            var visual = state["visual"] as JObject;
            if (prefab == null && visual?["model"] == null)
            {
                prefab = defaultPrefab;
                var merged = item["visual"]?.DeepClone() as JObject ?? new JObject();
                if (visual != null) merged.Merge(visual);
                visual = merged;
            }
            if (motion != null)
            {
                visual = visual?.DeepClone() as JObject ?? new JObject();
                visual["animation"] = motion;
            }
            var variant = Create(room, parent, prefab, visual, motion != null);
            variant.name = "状态 · " + id;
            return variant;
        }

        internal static GameObject Create(JObject room, Transform parent, string prefab, JObject visual = null, bool sampleExternally = false)
        {
            var variant = visual?["model"] == null && prefab != null ? Instantiate(Resources.Load<GameObject>(prefab), parent, false) : new GameObject();
            if (variant.transform.parent != parent) variant.transform.SetParent(parent, false);
            variant.hideFlags = HideFlags.DontSave;
            if (visual != null && visual.HasValues) variant.AddComponent<ImportedAppearance>().Apply(room, visual, sampleExternally);
            return variant;
        }
    }
}
