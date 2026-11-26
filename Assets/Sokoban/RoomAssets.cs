/**
 * @file RoomAssets.cs
 * @brief 资源内容缓存与导入校验，房间只持有 Core.AssetDescriptor，字节内容缓存留在 Unity 侧；单格模型的 1×1 格包围盒由 ImportedAppearance 在加载后实测。
 * Import(path, kind)：读取外部文件，校验扩展名与负载，按摘要写入内容缓存，返回描述符，ID 为类型加摘要。
 * Validate(assets)：逐项校验描述符的类型已登记、扩展名与类型匹配、摘要为 64 位小写十六进制、ID 与类型加摘要一致、ID 不重复。
 * Bytes(asset)：按摘要从内容缓存取字节并核对摘要，不使用原始路径。
 * Receive(assets, path)：从房间文件旁的 assets 目录取回并校验每份资源，再写入内容缓存。
 * Publish(assets, path)：校验描述符后把缓存内容原子写到房间文件旁的 assets 目录。
 * CheckFootprint(asset)：单格模型的水平包围盒必须是 1×1 格（容差 0.001），由 ImportedAppearance 离屏加载后实测，不合格抛 InvalidOperationException，非模型资源直接通过。
 * Store 先写完整临时文件再移动，并在并发写入时核对已存在的文件；Digest 对原始字节取 SHA-256。
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using Sokoban.Core;
using UnityEngine;

namespace Sokoban
{
    public static class RoomAssets
    {
        static string Cache => Path.Combine(Application.persistentDataPath, "asset-cache-v1");

        public static System.Threading.Tasks.Task CheckFootprint(AssetDescriptor asset) =>
            asset.Kind == AssetKinds.Model ? ImportedAppearance.InspectModel(asset) : System.Threading.Tasks.Task.CompletedTask;

        static void Check(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        public static AssetDescriptor Import(string path, string kind)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".jpeg") extension = ".jpg";
            string hash = Digest(bytes);
            var asset = new AssetDescriptor(kind + "-" + hash, kind, Path.GetFileNameWithoutExtension(path), hash, extension);
            CheckDescriptor(asset);
            CheckPayload(asset, bytes);
            Store(Cache, asset, bytes);
            return asset;
        }

        static string Digest(byte[] bytes)
        {
            using (var alg = SHA256.Create()) return BitConverter.ToString(alg.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        static string Filename(AssetDescriptor asset) => asset.Sha256 + asset.Extension;

        static void CheckDescriptor(AssetDescriptor asset)
        {
            Check(asset != null, "素材记录格式错误");
            Check(Registries.AssetKinds.TryGet(asset.Kind, out var info), "未知素材类型 " + asset.Kind);
            Check(info.Extensions.Contains(asset.Extension), "素材文件类型不匹配 " + asset.Extension);
            string hash = asset.Sha256;
            Check(hash != null && hash.Length == 64 && hash.All(c => "0123456789abcdef".Contains(c)) && asset.Id == asset.Kind + "-" + hash, "素材 ID 或摘要无效");
        }

        static void CheckPayload(AssetDescriptor asset, byte[] bytes)
        {
            if (asset.Extension == ".png" || asset.Extension == ".jpg")
            {
                bool signature = asset.Extension == ".png"
                    ? bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                    : bytes.Length > 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
                Check(signature, "图像文件类型不匹配");
                var texture = new Texture2D(2, 2);
                try { Check(texture.LoadImage(bytes, true), "图像解码失败"); }
                finally { if (Application.isPlaying) UnityEngine.Object.Destroy(texture); else UnityEngine.Object.DestroyImmediate(texture); }
                return;
            }
            if (asset.Extension == ".lua") return;
            Check(bytes.Length >= 20 && Encoding.ASCII.GetString(bytes, 0, 4) == "glTF" && BitConverter.ToUInt32(bytes, 4) == 2 && BitConverter.ToUInt32(bytes, 8) == bytes.Length && Encoding.ASCII.GetString(bytes, 16, 4) == "JSON", "需要完整的 GLB 2.0");
            uint length = BitConverter.ToUInt32(bytes, 12);
            Check(length <= bytes.Length - 20, "GLB 描述长度无效");
            var gltf = JObject.Parse(Encoding.UTF8.GetString(bytes, 20, (int)length));
            foreach (var entry in new[] { "buffers", "images" }.SelectMany(key => gltf[key] as JArray ?? new JArray()))
            {
                string uri = (string)entry["uri"];
                Check(uri == null || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase), "请将 GLB 的外部缓冲区和贴图打包进模型");
            }
            string required = asset.Kind == AssetKinds.Animation ? "animations" : "meshes";
            Check(gltf[required] is JArray items && items.Count > 0, "GLB 缺少 " + required);
        }

        public static void Validate(IEnumerable<AssetDescriptor> assets)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asset in assets ?? Enumerable.Empty<AssetDescriptor>())
            {
                CheckDescriptor(asset);
                Check(seen.Add(asset.Id), "素材表 ID 重复：" + asset.Id);
            }
        }

        public static byte[] Bytes(AssetDescriptor asset)
        {
            CheckDescriptor(asset);
            return VerifiedBytes(Cache, asset);
        }

        static byte[] VerifiedBytes(string folder, AssetDescriptor asset)
        {
            string file = Path.Combine(folder, Filename(asset));
            Check(File.Exists(file), "缺少素材 " + asset.Name + "：" + file);
            var bytes = File.ReadAllBytes(file);
            Check(Digest(bytes) == asset.Sha256, "素材校验失败：" + file);
            return bytes;
        }

        static void Store(string folder, AssetDescriptor asset, byte[] bytes)
        {
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, Filename(asset));
            if (File.Exists(file)) { VerifiedBytes(folder, asset); return; }
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temp, bytes);
                try { File.Move(temp, file); }
                catch (IOException) when (File.Exists(file)) { VerifiedBytes(folder, asset); }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        static string Sidecar(string path) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)), "assets");

        public static void Receive(IEnumerable<AssetDescriptor> assets, string path)
        {
            Validate(assets);
            foreach (var asset in assets ?? Enumerable.Empty<AssetDescriptor>())
            {
                var bytes = VerifiedBytes(Sidecar(path), asset);
                CheckPayload(asset, bytes);
                Store(Cache, asset, bytes);
            }
        }

        public static void Publish(IEnumerable<AssetDescriptor> assets, string path)
        {
            Validate(assets);
            foreach (var asset in assets ?? Enumerable.Empty<AssetDescriptor>()) Store(Sidecar(path), asset, Bytes(asset));
        }
    }
}
