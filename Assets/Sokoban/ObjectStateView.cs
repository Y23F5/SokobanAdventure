/**
 * @file ObjectStateView.cs
 * @brief 在稳定变换之下渲染对象当前状态的外观，状态表取自 Core.ObjectStateTable，外观引用解析为内置 Prefab 或本房导入模型。
 * Configure(table, home, look, fallbackPrefab, assets)：绑定状态表、初始格的世界位置、对象自身外观、兜底 Prefab 与资源表，并显示初始状态。
 * Show(state)：切换到状态外观，同一状态的外观实例缓存复用，状态为空时回到初始状态。
 * CreateVariant(state, parent)：状态名经 ObjectStateProfile.StateLook 取外观，解析不了时沿用对象自身外观，再退到兜底 Prefab。
 * CreateRemnant(look, parent)：按残留物外观引用建一个纯表现物，不参与任何规则。
 * Create(assets, parent, prefab, modelId, animationId, sampleExternally)：内置 Prefab 与导入模型共用的实例化入口。
 */
using System.Collections.Generic;
using System.Linq;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban
{
    public sealed class ObjectStateView : MonoBehaviour
    {
        public ObjectStateTable Table { get; private set; }
        public Vector3 Home { get; private set; }
        IReadOnlyList<AssetDescriptor> assets;
        AppearanceRef ownLook;
        string fallbackPrefab;
        GameObject visible;
        readonly Dictionary<string, GameObject> variants = new Dictionary<string, GameObject>();

        internal void Configure(ObjectStateTable table, Vector3 home, AppearanceRef look, string fallback, IReadOnlyList<AssetDescriptor> library)
        {
            Table = table;
            Home = home;
            ownLook = look;
            fallbackPrefab = fallback;
            assets = library ?? new List<AssetDescriptor>();
            Show(table.Initial);
        }

        internal void Show(string state)
        {
            state ??= Table.Initial;
            if (!variants.TryGetValue(state, out var variant)) variants.Add(state, variant = CreateVariant(state, transform));
            if (visible == variant) return;
            if (visible) visible.SetActive(false);
            visible = variant;
            visible.SetActive(true);
        }

        GameObject Build(AppearanceRef look, Transform parent)
        {
            string id = look?.Id;
            if (id == null) return null;
            var entry = AppearanceLibrary.Find(id);
            if (entry != null) return Create(assets, parent, entry.Prefab(look.Variant));
            if (assets.Any(a => a.Id == id && a.Kind == AssetKinds.Model)) return Create(assets, parent, null, id);
            return null;
        }

        internal GameObject CreateVariant(string state, Transform parent)
        {
            var variant = Build(new AppearanceRef(ObjectStateProfile.StateLook(state)), parent) ?? Build(ownLook, parent) ?? Create(assets, parent, fallbackPrefab);
            variant.name = "状态 · " + state;
            return variant;
        }

        internal GameObject CreateRemnant(AppearanceRef look, Transform parent) => Build(look, parent) ?? Create(assets, parent, null);

        internal static GameObject Create(IReadOnlyList<AssetDescriptor> library, Transform parent, string prefab, string modelId = null, string animationId = null, bool sampleExternally = false)
        {
            var variant = modelId == null && prefab != null ? Instantiate(Resources.Load<GameObject>(prefab), parent, false) : new GameObject();
            if (variant.transform.parent != parent) variant.transform.SetParent(parent, false);
            variant.hideFlags = HideFlags.DontSave;
            if (modelId != null || animationId != null) variant.AddComponent<ImportedAppearance>().Apply(library, modelId, animationId, null, sampleExternally);
            return variant;
        }
    }
}
