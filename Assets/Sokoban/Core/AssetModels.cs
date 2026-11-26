/**
 * @file AssetModels.cs
 * @brief 资源类型注册表与房间资源描述符：Core 只持有描述符，字节内容缓存留在表现层。
 * AssetKindInfo(id, extensions)：资源类型描述，声明可接受的扩展名。
 * AssetKinds：model、animation、texture、uiImage、script 五种资源类型 ID，CreateDefault 登记全部五种及各自扩展名。
 * AssetDescriptor(id, kind, name, sha256, extension)：一份资源的描述符，ID 由类型与摘要组成，房间文件只引用它。
 */
using System.Collections.Generic;

namespace Sokoban.Core
{
    public sealed class AssetKindInfo
    {
        public string Id { get; }
        public IReadOnlyList<string> Extensions { get; }

        public AssetKindInfo(string id, IReadOnlyList<string> extensions)
        {
            Id = id;
            Extensions = extensions ?? new List<string>();
        }
    }

    public static class AssetKinds
    {
        public const string Model = "model";
        public const string Animation = "animation";
        public const string Texture = "texture";
        public const string UiImage = "uiImage";
        public const string Script = "script";

        public static Registry<AssetKindInfo> CreateDefault()
        {
            var registry = new Registry<AssetKindInfo>("asset");
            registry.Register(Model, new AssetKindInfo(Model, new[] { ".glb" }));
            registry.Register(Animation, new AssetKindInfo(Animation, new[] { ".glb" }));
            registry.Register(Texture, new AssetKindInfo(Texture, new[] { ".png", ".jpg" }));
            registry.Register(UiImage, new AssetKindInfo(UiImage, new[] { ".png", ".jpg" }));
            registry.Register(Script, new AssetKindInfo(Script, new[] { ".lua" }));
            return registry;
        }
    }

    public sealed class AssetDescriptor
    {
        public string Id { get; }
        public string Kind { get; }
        public string Name { get; }
        public string Sha256 { get; }
        public string Extension { get; }

        public AssetDescriptor(string id, string kind, string name, string sha256, string extension)
        {
            Id = id;
            Kind = kind;
            Name = name;
            Sha256 = sha256;
            Extension = extension;
        }
    }
}
