/**
 * @file RoomAssets.cs
 * @brief Import reads an external file into the content cache and returns its portable descriptor.
 * ReadRoom/Receive verify dependencies before accepting a room. Publish writes verified immutable files beside the room.
 * Validate checks typed bindings and descriptor identities; Bytes resolves by digest without using the original path.
 * Store publishes a complete temporary file atomically and verifies concurrent writers. Digest hashes exact source bytes.
 */
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Sokoban
{
    public static class RoomAssets
    {
        static string Cache => Path.Combine(Application.persistentDataPath, "asset-cache-v1");
        public static JObject Import(string path, string kind)
        {
            byte[] bytes = File.ReadAllBytes(path);
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".jpeg") extension = ".jpg";
            string hash = Digest(bytes);
            var asset = new JObject { ["id"] = kind + "-" + hash, ["kind"] = kind, ["name"] = Path.GetFileNameWithoutExtension(path), ["sha256"] = hash, ["extension"] = extension };
            CheckDescriptor(asset);
            CheckPayload(asset, bytes);
            Store(Cache, asset, bytes);
            return asset;
        }
        static string Digest(byte[] bytes)
        {
            using (var alg = SHA256.Create()) return BitConverter.ToString(alg.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        static string Filename(JToken asset) => (string)asset["sha256"] + (string)asset["extension"];
        static void CheckDescriptor(JToken asset)
        {
            RoomFile.Check(asset is JObject, "素材记录格式错误");
            var kind = (string)asset["kind"];
            var hash = (string)asset["sha256"];
            var ext = (string)asset["extension"];
            bool model = kind == "model" || kind == "animation";
            RoomFile.Check(model || kind == "texture" || kind == "uiImage", "未知素材类型 " + kind);
            RoomFile.Check(model ? ext == ".glb" : ext == ".png" || ext == ".jpg", "素材文件类型不匹配 " + ext);
            RoomFile.Check(hash != null && hash.Length == 64 && hash.All(c => "0123456789abcdef".Contains(c)) && (string)asset["id"] == kind + "-" + hash, "素材 ID 或摘要无效");
        }
        static void CheckPayload(JToken asset, byte[] bytes)
        {
            string extension = (string)asset["extension"];
            if (extension == ".png" || extension == ".jpg")
            {
                bool signature = extension == ".png"
                    ? bytes.Take(8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})
                    : bytes.Length > 3 && bytes[0]==255 && bytes[1]==216 && bytes[2]==255;
                RoomFile.Check(signature, "图像文件类型不匹配");
                var texture = new Texture2D(2,2);
                try { RoomFile.Check(texture.LoadImage(bytes,true), "图像解码失败"); }
                finally { if(Application.isPlaying) UnityEngine.Object.Destroy(texture); else UnityEngine.Object.DestroyImmediate(texture); }
                return;
            }
            RoomFile.Check(bytes.Length >= 20 && Encoding.ASCII.GetString(bytes,0,4)=="glTF" && BitConverter.ToUInt32(bytes,4)==2 && BitConverter.ToUInt32(bytes,8)==bytes.Length && Encoding.ASCII.GetString(bytes,16,4)=="JSON", "需要完整的 GLB 2.0");
            uint length=BitConverter.ToUInt32(bytes,12);
            RoomFile.Check(length <= bytes.Length-20, "GLB 描述长度无效");
            var gltf=JObject.Parse(Encoding.UTF8.GetString(bytes,20,(int)length));
            foreach (var entry in new[]{"buffers","images"}.SelectMany(key => gltf[key] as JArray ?? new JArray()))
            {
                string uri=(string)entry["uri"];
                RoomFile.Check(uri==null || uri.StartsWith("data:",StringComparison.OrdinalIgnoreCase), "请将 GLB 的外部缓冲区和贴图打包进模型");
            }
            string required=(string)asset["kind"]=="animation" ? "animations" : "meshes";
            RoomFile.Check(gltf[required] is JArray items && items.Count>0,"GLB 缺少 "+required);
        }
        public static void Validate(JObject room)
        {
            RoomFile.Check(room["assets"]==null || room["assets"] is JObject,"房间素材表无效");
            var assets=room["assets"] as JObject;
            if (assets!=null) foreach (var entry in assets.Properties())
            {
                CheckDescriptor(entry.Value);
                RoomFile.Check(entry.Name==(string)entry.Value["id"],"素材表键与 ID 不一致");
            }
            foreach (var item in RoomFile.Objects(room))
            {
                if (item["builtinModel"] != null) RoomFile.Check(item.Parent == room["boxes"] && Array.IndexOf(RoomDraft.BuiltinBoxes,(string)item["builtinModel"]) >= 0 && item["visual"]?["model"] == null,"箱体模型绑定无效");
                if (item.Parent == room["decorations"]) RoomFile.Check(item["visual"] is JObject decoration && decoration["model"]!=null,"装饰缺少模型绑定");
                if (item["visual"]==null) continue;
                RoomFile.Check(item["visual"] is JObject,"对象外观格式无效");
                foreach (var binding in ((JObject)item["visual"]).Properties())
                {
                    RoomFile.Check(binding.Name=="model" || binding.Name=="texture" || binding.Name=="animation","未知外观绑定 "+binding.Name);
                    RequireBinding(assets,binding.Name,binding.Value,(string)item["id"]);
                }
            }
            if(room["uiImage"]!=null) RequireBinding(assets,"uiImage",room["uiImage"],(string)room["levelId"]);
        }
        static void RequireBinding(JObject assets,string kind,JToken id,string owner)
        {
            RoomFile.Check(id.Type==JTokenType.String && assets?[(string)id] is JObject entry && (string)entry["kind"]==kind, owner+" 缺少绑定素材 "+id);
        }
        public static byte[] Bytes(JToken asset)
        {
            CheckDescriptor(asset);
            return VerifiedBytes(Cache,asset);
        }
        static byte[] VerifiedBytes(string folder,JToken asset)
        {
            string file=Path.Combine(folder,Filename(asset));
            RoomFile.Check(File.Exists(file),"缺少素材 "+asset["name"]+"："+file);
            var bytes=File.ReadAllBytes(file);
            RoomFile.Check(Digest(bytes)==(string)asset["sha256"],"素材校验失败："+file);
            return bytes;
        }
        static void Store(string folder,JToken asset,byte[] bytes)
        {
            Directory.CreateDirectory(folder);
            string file=Path.Combine(folder,Filename(asset));
            if(File.Exists(file)){VerifiedBytes(folder,asset);return;}
            string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllBytes(temp,bytes);
                try { File.Move(temp,file); }
                catch(IOException) when(File.Exists(file)) { VerifiedBytes(folder,asset); }
            }
            finally { if(File.Exists(temp)) File.Delete(temp); }
        }
        static string Sidecar(string path) => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)),"assets");
        public static JObject ReadRoom(string path)
        {
            var room=RoomFile.Parse(File.ReadAllText(path));
            Receive(room,path);
            return room;
        }
        public static void Receive(JObject room,string path)
        {
            if(!(room["assets"] is JObject assets)) return;
            foreach(var asset in assets.Properties())
            {
                var bytes=VerifiedBytes(Sidecar(path),asset.Value);
                CheckPayload(asset.Value,bytes);
                Store(Cache,asset.Value,bytes);
            }
        }
        public static void Publish(JObject room,string path)
        {
            Validate(room);
            if(room["assets"] is JObject assets) foreach(var entry in assets.Properties()) Store(Sidecar(path),entry.Value,Bytes(entry.Value));
        }
    }
}
