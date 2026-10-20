using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace McDl.Core
{
    /// <summary>
    /// 一种整合包格式的读取器。每种格式一个类，互不知道彼此；
    /// 加一种新格式 = 加一个实现 + 在 <see cref="PackFormats"/> 里登记。
    /// 格式细节见 docs/packs.md。
    /// </summary>
    public interface IPackFormat
    {
        /// <summary>短 id，出现在日志与报错里。</summary>
        string Id { get; }

        /// <summary>给人看的名字。</summary>
        string DisplayName { get; }

        /// <summary>这个包是不是本格式。只允许读目录信息，不要解压整个包。</summary>
        bool CanRead(string archivePath);

        /// <summary>读成统一模型。格式不对时抛 <see cref="InvalidDataException"/>。</summary>
        PackManifest Read(string archivePath);
    }

    /// <summary>格式登记表：按顺序试，第一个认领的格式负责解析。</summary>
    public static class PackFormats
    {
        /// <summary>注意顺序：modrinth 与 curseforge 的索引文件名不一样，谁都不会抢谁的。</summary>
        public static readonly IReadOnlyList<IPackFormat> All = new IPackFormat[]
        {
            new ModrinthPackFormat(),
            new CurseForgePackFormat(),
            new McbbsPackFormat(),
            new HmclPackFormat()
        };

        public static PackManifest Read(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath))
                throw new ArgumentException("整合包路径不能为空", nameof(archivePath));
            if (!File.Exists(archivePath))
                throw new FileNotFoundException("找不到整合包：" + archivePath, archivePath);

            var known = new List<string>();
            foreach (var format in All)
            {
                known.Add(format.DisplayName);
                if (!format.CanRead(archivePath)) continue;
                return format.Read(archivePath);
            }

            throw new InvalidDataException(
                "无法识别这个整合包（" + Path.GetFileName(archivePath) + "）。目前支持：" + string.Join("、", known));
        }
    }

    /// <summary>几个格式共用的 zip 小工具。</summary>
    internal static class PackArchive
    {
        /// <summary>按候选名字在压缩包里找条目，找不到返回 null。顺序即优先级。</summary>
        public static ZipArchiveEntry FindEntry(ZipArchive archive, params string[] names)
        {
            foreach (var name in names)
            {
                var direct = archive.GetEntry(name);
                if (direct != null) return direct;

                var nested = archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("/" + name, StringComparison.OrdinalIgnoreCase));
                if (nested != null) return nested;
            }

            return null;
        }

        public static string ReadText(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        public static T ReadJson<T>(ZipArchiveEntry entry) where T : class
        {
            return Json.Parse<T>(ReadText(entry));
        }
    }
}
