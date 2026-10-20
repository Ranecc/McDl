using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace McDl.Core
{
    /// <summary>
    /// 整合包读出来之后的统一形状：不管是 Modrinth、CurseForge、MCBBS 还是 HMCL，
    /// 上层只看这一个对象。详见 docs/packs.md。
    /// </summary>
    public sealed class PackManifest
    {
        /// <summary>识别出来的格式 id，见 <see cref="PackFormats"/>。</summary>
        public string FormatId { get; set; }

        /// <summary>整合包名，可能为空。</summary>
        public string Name { get; set; }

        /// <summary>整合包自己的版本号，可能为空。</summary>
        public string VersionId { get; set; }

        /// <summary>对应的原版版本，必填；为空说明这个包没写清楚。</summary>
        public string MinecraftVersion { get; set; }

        /// <summary>加载器；纯原版包是 <see cref="LoaderSpec.None"/>。</summary>
        public LoaderSpec Loader { get; set; }

        /// <summary>包里的文件清单（模组、配置等）。当前只做展示与统计，不下载。</summary>
        public List<PackFileEntry> Files { get; set; }

        /// <summary>整合包来源，绝对路径。</summary>
        public string SourcePath { get; set; }

        public PackManifest()
        {
            Files = new List<PackFileEntry>();
            Loader = LoaderSpec.None;
        }

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name)) return "整合包";
                return string.IsNullOrWhiteSpace(VersionId) ? Name.Trim() : Name.Trim() + " " + VersionId.Trim();
            }
        }

        /// <summary>把包名变成可以当文件夹名的版本 id：“微光 1.2” → “微光-1.2”。</summary>
        public string BuildProfileId()
        {
            var raw = string.IsNullOrWhiteSpace(Name) ? "modpack" : Name.Trim();
            if (!string.IsNullOrWhiteSpace(VersionId)) raw += "-" + VersionId.Trim();

            var invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(raw.Length);
            foreach (var ch in raw)
            {
                if (Array.IndexOf(invalid, ch) >= 0 || ch == ' ' || ch == '/' || ch == '\\') builder.Append('-');
                else builder.Append(ch);
            }

            var id = builder.ToString().Trim('-', '.');
            return id.Length == 0 ? "modpack" : id;
        }

        /// <summary>把若干文件累加成一个总体积，给 UI 显示用。</summary>
        public long TotalSize
        {
            get { return Files == null ? 0 : Files.Where(f => f != null).Sum(f => f.Size); }
        }
    }

    /// <summary>包里的一条文件记录。</summary>
    public sealed class PackFileEntry
    {
        /// <summary>相对整合包根目录的路径。</summary>
        public string Path { get; set; }

        /// <summary>SHA1，可能为空。</summary>
        public string Sha1 { get; set; }

        /// <summary>下载直链，可能为空（老格式只给 CurseForge project/file id）。</summary>
        public List<string> Downloads { get; set; }

        /// <summary>文件大小，未知时是 0。</summary>
        public long Size { get; set; }

        public PackFileEntry()
        {
            Downloads = new List<string>();
        }
    }
}
