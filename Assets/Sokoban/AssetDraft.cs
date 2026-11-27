/**
 * @file AssetDraft.cs
 * @brief 资源登记、资源绑定与墙外装饰、自定义物件的放置入口，全部经 Core.RoomDraftModel 与 Commit 工作，不再读写 JObject。
 * ModelEdit(apply, revert)：把一对闭包包成 ICommand，让草稿上的组合修改作为一条可撤销命令提交。
 * AssetHold(asset)：把资源描述符并入草稿资源表，Apply 只在尚未登记时加入，Revert 只撤销自己加入的那一份，内置外观没有描述符时为空操作。
 * ObjectTemplates.Find(model, id)：按模板 ID 取草稿里的对象模板，没有时返回空。
 * RoomDraft.AssetDefinition(id)、AssetCopy(id)、BrushAsset：按 ID 取本房资源描述符；AssetCopy 生成对应的 AssetHold；BrushAsset 是画刷外观为本房资源时的资源 ID。
 * RoomDraft.ImportAsset(path, kind)、RegisterAsset(asset)：经 RoomAssets 导入文件，再以一条可撤销命令登记描述符，已登记时不提交。
 * GetRecoveryPoseSource()、GetDefaultRecoveryPoseSource()：读取装置当前或默认演出 Lua。
 * SetRecoveryPoseSource(source)：校验 Lua，登记脚本素材与装置引用，两者作为一次编辑撤销。
 * RoomDraft.CanBindAsset(id, categoryId)、AssetBindingTarget(id)、BindAsset(id, categoryId)：界面图绑定到房间，模型经 ReplaceAppearance 绑定到所选对象或地形格，类别必须与所选内容一致。
 * RoomDraft.SelectAsset(id, categoryId)：选择模型作为装饰或自定义物件画刷，不修改房间。
 * RoomDraft.PlaceAsset(cell)：装饰画刷只能放在房间外并登记装饰，不占房内格；自定义画刷放自定义对象，模板画刷同时登记只含模板 ID 的实例，占房内格并先经 PlacementRules 预检。
 * RoomDraft.BoxSelected、BindBuiltinBox(model)：所选是箱子时把外观换成内置箱体。
 */
using System;
using System.Linq;
using Sokoban.Core;

namespace Sokoban
{
    public sealed class ModelEdit : ICommand
    {
        readonly Action apply, revert;

        public ModelEdit(Action apply, Action revert)
        {
            this.apply = apply;
            this.revert = revert;
        }

        public void Apply() => apply();

        public void Revert() => revert();
    }

    public sealed class AssetHold
    {
        readonly AssetDescriptor asset;
        bool added;

        public AssetHold(AssetDescriptor asset) { this.asset = asset; }

        public void Apply(RoomDraftModel model)
        {
            if (asset == null || model.Assets.Any(a => a.Id == asset.Id)) return;
            model.Assets.Add(asset);
            added = true;
        }

        public void Revert(RoomDraftModel model)
        {
            if (!added) return;
            model.Assets.Remove(asset);
            added = false;
        }
    }

    public static class ObjectTemplates
    {
        public static ObjectTemplate Find(RoomDraftModel model, string id) =>
            id != null && model.Templates.TryGetValue(id, out var template) ? template : null;
    }

    public sealed partial class RoomDraft
    {
        public static readonly string[] BuiltinBoxes = { "Cargo", "Machinery", "Logs" };
        public static readonly string[] BuiltinBoxNames = { "仓库木箱", "工厂货箱", "森林原木" };

        public int PlacementRevision { get; private set; } = -1;
        public string PlacedObject { get; private set; }

        void NotifyPlacement(string id) { PlacedObject = id; PlacementRevision = Revision; }

        public AssetDescriptor AssetDefinition(string id) => id == null ? null : Model.Assets.FirstOrDefault(a => a.Id == id);

        AssetHold AssetCopy(string id) => new AssetHold(AssetDefinition(id));

        public string BrushAsset => BrushAppearance != null && AssetDefinition(BrushAppearance.Id) != null ? BrushAppearance.Id : null;

        public string ImportAsset(string path, string kind) => RegisterAsset(RoomAssets.Import(path, kind));

        public string GetRecoveryPoseSource()
        {
            var device = Model.RecoveryDevice ?? throw new InvalidOperationException("缺少恢复装置");
            return RoomAssets.RecoveryScriptSource(device.ScriptRef, Model.Assets);
        }

        public string GetDefaultRecoveryPoseSource()
        {
            var device = Model.RecoveryDevice ?? throw new InvalidOperationException("缺少恢复装置");
            return RoomAssets.RecoveryScriptSource(RecoveryDevice.DefaultScriptRef(device.Kind), Model.Assets);
        }

        public void SetRecoveryPoseSource(string source)
        {
            var model = Model;
            var before = model.RecoveryDevice ?? throw new InvalidOperationException("缺少恢复装置");
            LuaRecoveryMethod.ValidateSource(source);
            var asset = RoomAssets.StoreRecoveryScript(source);
            if (asset.Id == before.ScriptRef) return;
            var after = new RecoveryDevice(before.Id, before.Kind, before.Anchor, before.Rotation, asset.Id,
                before.Duration, before.Params, before.ItemName, before.ItemDescription, before.ItemAcquiredHint);
            var hold = new AssetHold(asset);
            Commit(new ModelEdit(() =>
            {
                model.ReplaceRecoveryDevice(after);
                hold.Apply(model);
            }, () =>
            {
                model.ReplaceRecoveryDevice(before);
                hold.Revert(model);
            }), before.Id);
        }

        internal string RegisterAsset(AssetDescriptor asset)
        {
            var model = Model;
            if (model.Assets.Any(a => a.Id == asset.Id)) return asset.Id;
            Commit(new ModelEdit(() => model.Assets.Add(asset), () => model.Assets.Remove(asset)));
            return asset.Id;
        }

        public bool CanBindAsset(string id, string categoryId = null)
        {
            var asset = AssetDefinition(id);
            if (asset == null) return false;
            if (asset.Kind == AssetKinds.UiImage) return true;
            if (asset.Kind != AssetKinds.Model || !SelectedCell.HasValue) return false;
            return categoryId == null || categoryId == SelectedCategory;
        }

        public string AssetBindingTarget(string id)
        {
            if (!CanBindAsset(id)) return "选择地图上的物件或地形格后，可为它绑定素材";
            if (AssetDefinition(id).Kind == AssetKinds.UiImage) return "绑定位置：当前房间界面";
            var cell = SelectedCell.Value;
            return "绑定位置：" + AppearanceLibrary.TypeName(SelectedCategory) + " " + cell;
        }

        public void BindAsset(string id, string categoryId = null)
        {
            var asset = AssetDefinition(id) ?? throw new InvalidOperationException("当前房间没有所选素材");
            if (!CanBindAsset(id, categoryId)) throw new InvalidOperationException("请选择同类型的对象或地形格");
            if (asset.Kind != AssetKinds.UiImage)
            {
                ReplaceAppearance(id);
                return;
            }
            var model = Model;
            string before = model.UiImageAssetId;
            if (before == id) return;
            Commit(new ModelEdit(() => model.UiImageAssetId = id, () => model.UiImageAssetId = before));
        }

        public void SelectAsset(string id, string categoryId = AppearanceLibrary.Decoration)
        {
            if (AssetDefinition(id)?.Kind != AssetKinds.Model) throw new InvalidOperationException("放置工具需要模型素材");
            if (categoryId != AppearanceLibrary.Decoration && categoryId != ObjectCategories.Custom)
                throw new InvalidOperationException("此页签不支持模型素材");
            ChooseAppearance(id, categoryId);
        }

        public void PlaceAsset(Cell cell)
        {
            var model = Model;
            var room = model.Room;
            if (BrushCategory == null || BrushAppearance == null && BrushTemplate == null) throw new InvalidOperationException("请先选择素材");
            if (BrushCategory == AppearanceLibrary.Decoration)
            {
                if (RecoveryFloorBlocksDecoration(cell)) throw new InvalidOperationException("恢复装置地板不可放置装饰");
                if (room.Inside(cell)) throw new InvalidOperationException("装饰只能放在房间外");
                string decorId = "decor-" + Guid.NewGuid().ToString("N");
                var decoration = new Decoration(decorId, cell, BrushAppearance, BrushRotation);
                var decorHold = AssetCopy(BrushAppearance.Id);
                Commit(new ModelEdit(() =>
                {
                    decorHold.Apply(model);
                    room.Ids.Add(decorId, "decoration");
                    model.Decorations.Add(decoration);
                }, () =>
                {
                    model.Decorations.Remove(decoration);
                    room.Ids.Remove(decorId);
                    decorHold.Revert(model);
                }));
                NotifyPlacement(decorId);
                return;
            }
            string error = PlacementRules.Error(model, ObjectCategories.Custom, cell, null);
            if (error != null) throw new InvalidOperationException("格子 " + cell + " 不能放置：" + error);
            var template = ObjectTemplates.Find(model, BrushTemplate);
            if (BrushTemplate != null && template == null) throw new InvalidOperationException("当前房间没有所选对象模板");
            string id = "custom-" + Guid.NewGuid().ToString("N");
            var look = template?.Appearance ?? BrushAppearance;
            int rotation = BrushRotation;
            var hold = AssetCopy(look?.Id);
            var instance = template == null ? null : new TemplateInstance(id, template.Id);
            Commit(new ModelEdit(() =>
            {
                hold.Apply(model);
                room.AddObject(id, ObjectCategories.Custom, cell, look, rotation);
                if (instance != null) model.Instances.Add(instance);
            }, () =>
            {
                if (instance != null) model.Instances.Remove(instance);
                room.RemoveObject(id);
                hold.Revert(model);
            }), id);
            NotifyPlacement(id);
        }

        public bool BoxSelected => SelectedCategory == ObjectCategories.Box;

        public void BindBuiltinBox(string builtin)
        {
            if (!BoxSelected || Array.IndexOf(BuiltinBoxes, builtin) < 0) throw new InvalidOperationException("请选择箱子和内置箱体模型");
            ReplaceAppearance(builtin);
        }
    }
}
