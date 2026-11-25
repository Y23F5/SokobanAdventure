/**
 * @file ForestRecovery.cs
 * @brief Sample plays the source wood decay clip at the displaced grid cell and grows the tree at the recovery cell. Clear removes the transient models and restores the cargo.
 */
using System.Linq;
using UnityEngine;

namespace Sokoban
{
    internal sealed class ForestRecovery
    {
        readonly GameObject fragments, animated, tree, departing;
        readonly Transform cargo;
        readonly AnimationClip clip;
        readonly AnimationClip growthClip;
        readonly ImportedAppearance departureMotion, arrivalMotion;
        readonly Renderer[] visibleCargo;
        internal ForestRecovery(Transform parent,Transform cargo,Vector3 home)
            : this(parent, cargo, home, Object.Instantiate(Resources.Load<GameObject>("Diorama/Tree"), parent, false), true) { }

        internal ForestRecovery(Transform parent, Transform cargo, Vector3 home, GameObject arrival, bool useDecay,
            GameObject departure = null, bool grow = true)
        {
            this.cargo = cargo;
            visibleCargo = cargo.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).ToArray();
            if (useDecay)
            {
                fragments = new GameObject("原木腐朽");
                fragments.transform.SetParent(parent,false);
                animated = Object.Instantiate(Resources.Load<GameObject>("Diorama/ForestDecay"),fragments.transform,false);
                fragments.transform.position = cargo.position;
                var clips = Resources.LoadAll<AnimationClip>("Diorama/ForestDecay");
                clip = clips.First(c=>!c.name.StartsWith("__preview__"));
                var animator = animated.GetComponent<Animator>();
                if (animator) animator.enabled=false;
                foreach (var renderer in fragments.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var imported = renderer.sharedMaterial;
                    string name = imported ? imported.name : "";
                    int marker = name.IndexOf('#');
                    string hex = marker >= 0 && name.Length >= marker+7 ? name.Substring(marker+1,6).ToUpperInvariant() : "71452F";
                    renderer.sharedMaterial = Resources.Load<Material>("Diorama/Paint-"+hex) ?? Resources.Load<Material>("Diorama/Paint-71452F");
                }
            }
            tree = arrival;
            departing = departure;
            if (departing)
            {
                departing.name = "恢复离场演出";
                departing.transform.position = cargo.position;
                departureMotion = departing.GetComponent<ImportedAppearance>();
            }
            if (tree)
            {
                if (grow) growthClip = Resources.Load<AnimationClip>("Diorama/TreeGrowth");
                else arrivalMotion = tree.GetComponent<ImportedAppearance>();
                tree.name = "恢复格再生";
                tree.transform.position = home;
            }
        }
        internal void Sample(float fraction)
        {
            bool cracked = fraction >= (fragments ? .09f : departing ? 0f : .605f);
            foreach (var renderer in visibleCargo) renderer.enabled = !cracked;
            if (departing)
            {
                departing.SetActive(fraction < .70f);
                if (departing.activeSelf) departureMotion.SampleMotion(fraction / .70f);
            }
            if (fragments)
            {
                fragments.SetActive(cracked && fraction < .70f);
                if (fragments.activeSelf) clip.SampleAnimation(animated,Mathf.Min(clip.length,12f/30f + fraction*4f));
            }
            if (tree)
            {
                float growth = Mathf.InverseLerp(.605f,.97f,fraction);
                tree.SetActive(growth > 0);
                if (arrivalMotion) arrivalMotion.SampleMotion(growth);
                else if (growthClip) growthClip.SampleAnimation(tree, growth * growthClip.length);
                else tree.transform.localScale = Vector3.one * Mathf.SmoothStep(0,1,growth);
            }
        }
        internal void Clear()
        {
            foreach (var renderer in visibleCargo) if(renderer) renderer.enabled=true;
            if (fragments) fragments.SetActive(false);
            if (tree) tree.SetActive(false);
            if (departing) departing.SetActive(false);
            Object.Destroy(fragments); Object.Destroy(tree); Object.Destroy(departing);
        }
    }
}
