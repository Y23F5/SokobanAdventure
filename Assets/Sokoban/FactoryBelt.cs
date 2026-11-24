/**
 * @file FactoryBelt.cs
 * @brief 构造器按起点与恢复格的距离布置导轨和滚筒。Sample 按恢复进度展开输送带、驱动滚筒和夹具，再将货箱放回地面。Clear 清理本次机构。
 */
using UnityEngine;

namespace Sokoban
{
    internal sealed class FactoryBelt
    {
        readonly Transform root, belt, grabber;
        readonly Transform[] rollers;
        readonly Vector3 start, end;
        readonly float length;
        internal FactoryBelt(Transform parent, Vector3 start, Vector3 end)
        {
            this.start = start;
            this.end = end;
            length = Vector3.Distance(start,end) + .72f;
            root = new GameObject("工厂回收机构").transform;
            root.SetParent(parent,false);
            root.SetPositionAndRotation(start,Quaternion.LookRotation(end-start));
            belt = new GameObject("伸缩输送带").transform;
            belt.SetParent(root,false);
            for (int sign=-1; sign<=1; sign+=2)
                Part("导轨",PrimitiveType.Cube,"D4A456",new Vector3(sign*.31f,.12f,length*.5f-.36f),new Vector3(.05f,.12f,length));
            int gaps = Mathf.CeilToInt(length/.18f);
            rollers = new Transform[gaps+1];
            for (int i=0; i<=gaps; i++)
            {
                rollers[i] = Part("滚筒",PrimitiveType.Cylinder,i%2==0 ? "87989B" : "5A737A",new Vector3(0,.14f,length*i/gaps-.36f),new Vector3(.12f,.29f,.12f));
                rollers[i].localRotation = Quaternion.Euler(0,0,90);
            }
            grabber = Object.Instantiate(Resources.Load<GameObject>("Diorama/Conveyor"),root,false).transform;
            grabber.name = "随行机械臂";
            foreach (Transform part in grabber)
                if (part.name=="87989B" || part.name=="5A737A" || part.name=="D4A456" || part.name=="D88B43") part.gameObject.SetActive(false);
        }
        Transform Part(string name, PrimitiveType shape,string color,Vector3 position,Vector3 scale)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = name;
            var collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Object.Destroy(collider);
            part.transform.SetParent(belt,false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = Resources.Load<Material>("Diorama/Paint-"+color);
            return part.transform;
        }
        static float Phase(float t,float a,float b) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(a,b,t));
        internal void Sample(Transform cargo,float t)
        {
            float travel = Phase(t,.30f,.78f);
            float lift = Phase(t,.17f,.30f)*(1-Phase(t,.79f,.91f));
            float extension = Phase(t,.04f,.24f)*(1-Phase(t,.84f,.99f));
            belt.localScale = new Vector3(1,Mathf.Max(.001f,extension),Mathf.Max(.001f,extension));
            cargo.position = Vector3.Lerp(start,end,travel)+Vector3.up*(lift*.26f);
            grabber.position = Vector3.Lerp(start,end,travel)+Vector3.up*(.65f+.46f*(1-lift)+.34f*Mathf.Sin(travel*Mathf.PI));
            grabber.localScale = Vector3.one*Mathf.Max(.001f,Phase(t,0,.1f)*(1-Phase(t,.91f,1)));
            var rotation = Quaternion.Euler(0,0,90)*Quaternion.AngleAxis(-travel*(length-.72f)*Mathf.Rad2Deg/.06f,Vector3.up);
            foreach (var roller in rollers) roller.localRotation = rotation;
        }
        internal void Clear() { root.gameObject.SetActive(false); Object.Destroy(root.gameObject); }
    }
}
