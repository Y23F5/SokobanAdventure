/**
 * @file WarehouseLift.cs
 * @brief Sample positions the perimeter carriage, telescoping boom and fork blades relative to the current load and its recovery cell. Clear parks the truck and releases the rig.
 */
using UnityEngine;

namespace Sokoban
{
    internal sealed class WarehouseLift
    {
        readonly Transform truck, root, tower, boom, mast, crossbar, left, right;
        readonly Vector3 parked;
        internal WarehouseLift(Transform truck)
        {
            this.truck = truck;
            parked = truck.position;
            root = new GameObject("叉车伸缩机构").transform;
            root.SetParent(truck.parent, false);
            tower = Beam("升降塔", "Lift-415251");
            boom = Beam("伸缩臂", "Lift-F4BB57");
            mast = Beam("升降架", "Lift-364442");
            crossbar = Beam("货叉架", "Lift-415251");
            left = Beam("左货叉", "Lift-6C7570");
            right = Beam("右货叉", "Lift-6C7570");
        }
        Transform Beam(string name, string meshName)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            var collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Object.Destroy(collider);
            part.transform.SetParent(root,false);
            var color = meshName.Substring(meshName.IndexOf('-') + 1);
            foreach (var renderer in truck.GetComponentsInChildren<MeshRenderer>())
                if (renderer.sharedMaterial && renderer.sharedMaterial.name.EndsWith(color))
                { part.GetComponent<Renderer>().sharedMaterial = renderer.sharedMaterial; break; }
            return part.transform;
        }
        static float Phase(float t,float start,float end) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,end,t));
        static void Pose(Transform part,Vector3 a,Vector3 b,float width,float depth)
        {
            var axis = b-a;
            part.position = (a+b)*.5f;
            part.rotation = axis.sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up,axis) : Quaternion.identity;
            part.localScale = new Vector3(width,Mathf.Max(.001f,axis.magnitude),depth);
        }
        internal void Sample(Transform cargo, Vector3 source, Vector3 home,float t)
        {
            float raise = Phase(t,0,.13f)-Phase(t,.93f,1);
            float reach = Phase(t,.12f,.23f)-Phase(t,.86f,.94f);
            float lower = Phase(t,.23f,.32f)-Phase(t,.79f,.86f);
            float insertion = Phase(t,.32f,.38f)-Phase(t,.73f,.79f);
            float lift = Phase(t,.38f,.47f)-Phase(t,.64f,.73f);
            var load = Vector3.Lerp(source,home,Phase(t,.47f,.64f));
            float z = Mathf.Lerp(parked.z,load.z,raise);
            truck.position = new Vector3(parked.x,parked.y,z);
            var mount = truck.position + new Vector3(-.4f,1.24f,0);
            var top = new Vector3(mount.x,Mathf.Lerp(mount.y+.21f,2.74f,raise),z);
            var tip = new Vector3(Mathf.Lerp(mount.x-.2f,load.x+.46f,reach),top.y,z);
            cargo.position = load + Vector3.up*(1.52f*lift);
            var fork = new Vector3(tip.x,Mathf.Lerp(top.y-.16f,cargo.position.y+.0935f,lower),z);
            Pose(tower,mount,top,.14f,.16f);
            Pose(boom,top,tip,.13f,.14f);
            Pose(mast,tip,fork,.062f,.092f);
            Pose(crossbar,fork-Vector3.forward*.19f,fork+Vector3.forward*.19f,.07f,.048f);
            var length = Vector3.left*Mathf.Lerp(.018f,.78f,insertion);
            var offset = Vector3.forward*.15f;
            Pose(left,fork-offset,fork-offset+length,.033f,.064f);
            Pose(right,fork+offset,fork+offset+length,.033f,.064f);
        }
        internal void Clear()
        {
            truck.position = parked;
            root.gameObject.SetActive(false);
            Object.Destroy(root.gameObject);
        }
    }
}
