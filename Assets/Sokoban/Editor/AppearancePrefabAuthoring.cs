/**
 * @file AppearancePrefabAuthoring.cs
 * @brief Install bakes factory and forest alternatives using the existing diorama palette. Forest stones follow the prototype's stacked faceted rocks.
 * Save creates editable prefabs for the common appearance library; Part adds local geometry with shared materials.
 */
using UnityEditor;
using UnityEngine;

namespace Sokoban.Editor
{
    public static class AppearancePrefabAuthoring
    {
        const string Folder = "Assets/Sokoban/Resources/LayoutModels/";

        public static void Install()
        {
            var factoryMetal = Resources.Load<Material>("LayoutModels/FactoryMetal");
            var stone = Resources.Load<Material>("LayoutModels/Stone");
            var moss = Resources.Load<Material>("LayoutModels/Moss");
            var wood = Resources.Load<Material>("Diorama/Paint-71452F");
            var warm = Resources.Load<Material>("Diorama/Paint-C58A54");
            Save("FactoryWall", root =>
            {
                Part(root, "金属底座", PrimitiveType.Cube, new Vector3(0,.29f,0), new Vector3(1,.58f,1), factoryMetal);
                Part(root, "金属上盖", PrimitiveType.Cube, new Vector3(0,.65f,0), new Vector3(.96f,.14f,.96f), factoryMetal);
                for (int i = -1; i <= 1; i++) Part(root, "金属压条", PrimitiveType.Cube, new Vector3(i*.29f,.73f,0), new Vector3(.09f,.04f,.96f), warm);
            });
            var rock = Rock();
            Save("ForestWall", root =>
            {
                var lower = Part(root,"青苔石基",PrimitiveType.Cube,new Vector3(0,.26f,0),new Vector3(1,.68f,1),moss);
                lower.GetComponent<MeshFilter>().sharedMesh = rock;
                var upper = Part(root,"叠石",PrimitiveType.Cube,new Vector3(.045f,.61f,-.045f),new Vector3(.80f,.54f,.78f),stone);
                upper.GetComponent<MeshFilter>().sharedMesh = rock;
                upper.localRotation = Quaternion.Euler(0,24,0);
            });
            Save("ForestFence", root =>
            {
                for (int i = -1; i <= 1; i++) Part(root,"篱笆桩",PrimitiveType.Cube,new Vector3(i*.41f,.26f,0),new Vector3(.18f,.52f,.18f),wood);
                for (int i = 1; i <= 2; i++) Part(root,"篱笆横木",PrimitiveType.Cube,new Vector3(0,i*.16f,.06f),new Vector3(1,.07f,.08f),warm);
            });
            Save("TreeStump", root =>
            {
                Part(root,"树桩",PrimitiveType.Cylinder,new Vector3(0,.11f,0),new Vector3(.4f,.11f,.4f),wood);
                Part(root,"截面",PrimitiveType.Cylinder,new Vector3(0,.228f,0),new Vector3(.34f,.009f,.34f),warm);
            });
            Save("FactoryTarget", root =>
            {
                Part(root,"工厂目标底座",PrimitiveType.Cube,new Vector3(0,.025f,0),new Vector3(.82f,.03f,.82f),stone);
                var mark = Part(root,"定位方标",PrimitiveType.Cube,new Vector3(0,.05f,0),new Vector3(.40f,.022f,.40f),warm);
                mark.localRotation = Quaternion.Euler(0,45,0);
            });
            Save("ForestTarget", root =>
            {
                Part(root,"森林目标底座",PrimitiveType.Cylinder,new Vector3(0,.030f,0),new Vector3(.78f,.02f,.78f),moss);
                Part(root,"木心标记",PrimitiveType.Cylinder,new Vector3(0,.057f,0),new Vector3(.47f,.009f,.47f),warm);
            });
            const string growthPath = "Assets/Sokoban/Resources/Diorama/TreeGrowth.anim";
            if (!AssetDatabase.LoadAssetAtPath<AnimationClip>(growthPath))
            {
                var clip = new AnimationClip { name = "生长", frameRate = 30 };
                foreach (string axis in new[] { "x", "y", "z" })
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalScale." + axis),
                        AnimationCurve.EaseInOut(0, 0, 1, 1));
                AssetDatabase.CreateAsset(clip, growthPath);
            }
            AssetDatabase.SaveAssets();
        }

        static Mesh Rock()
        {
            const string path = Folder + "ForestRock.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) return existing;
            const float t = .30901699f;
            var v = new[] { new Vector3(-t,.5f,0), new Vector3(t,.5f,0), new Vector3(-t,-.5f,0), new Vector3(t,-.5f,0),
                new Vector3(0,-t,.5f),new Vector3(0,t,.5f), new Vector3(0,-t,-.5f),new Vector3(0,t,-.5f),
                new Vector3(.5f,0,-t),new Vector3(.5f,0,t), new Vector3(-.5f,0,-t),new Vector3(-.5f,0,t) };
            int[] faces = { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            var vertices = new Vector3[faces.Length];
            var indices = new int[faces.Length];
            for (int i=0; i<faces.Length; i++) { vertices[i] = v[faces[i]]; indices[i] = i; }
            var mesh = new Mesh { name="Forest Rock", vertices=vertices, triangles=indices };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh,path);
            return mesh;
        }

        static void Save(string name, System.Action<Transform> build)
        {
            var root = new GameObject(name);
            build(root.transform);
            PrefabUtility.SaveAsPrefabAsset(root,Folder + name + ".prefab");
            Object.DestroyImmediate(root);
        }

        static Transform Part(Transform parent, string name, PrimitiveType type, Vector3 position, Vector3 size, Material material)
        {
            var piece = GameObject.CreatePrimitive(type);
            piece.name = name;
            piece.transform.SetParent(parent,false);
            piece.transform.localPosition = position;
            piece.transform.localScale = size;
            Object.DestroyImmediate(piece.GetComponent<Collider>());
            piece.GetComponent<Renderer>().sharedMaterial = material;
            return piece.transform;
        }
    }
}
