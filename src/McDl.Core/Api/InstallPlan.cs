using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace McDl.Core
{
    /// <summary>一次安装解析出来的全部内容：下载什么、解压什么、下载后还要做什么。</summary>
    /// <remarks>
    /// 计划由 <see cref="IInstallSource"/> 产出，自己负责落盘，但不知道来源怎么选的；
    /// 这样下载引擎不需要认识版本清单或整合包格式。时序见 docs/architecture.md。
    /// </remarks>
    public sealed class InstallPlan
    {
        /// <summary>目标 .minecraft 目录。</summary>
        public string Root { get; internal set; }

        /// <summary>接收下载内容的版本目录名。</summary>
        public string VersionId { get; internal set; }

        /// <summary>界面上显示的名字（版本号、整合包名等）。</summary>
        public string Label { get; internal set; }

        /// <summary>assets/indexes 下的索引 id；旧版本是 "legacy"。</summary>
        public string AssetIndexId { get; internal set; }

        /// <summary>解析后的版本 JSON；不基于原版元数据的计划为 null。</summary>
        public VersionDetail Detail { get; internal set; }

        /// <summary>解析后的资源索引；不下载 assets 时为 null。</summary>
        public AssetIndex Assets { get; internal set; }

        /// <summary>这次安装要落到磁盘的全部文件。</summary>
        public List<FileTask> Files { get; internal set; }

        /// <summary>下载完成后要解压的 natives 包。</summary>
        public List<NativesExtraction> Natives { get; internal set; }

        /// <summary>旧版本按名字引用资源，需要 assets/virtual/&lt;id&gt; 这种扁平布局。</summary>
        public bool VirtualAssets { get; internal set; }

        /// <summary>资源索引出现之前的版本读扁平的 resources 目录。</summary>
        public bool MapToResources { get; internal set; }

        /// <summary>
        /// 下载完成之后才做的工作，比如跑 Forge 官方 installer。
        /// 按注册顺序执行，出错会中断整个安装。
        /// </summary>
        public List<Func<CancellationToken, Task>> PostDownload { get; }

        public long TotalBytes => VanillaResolver.TotalBytes(Files);

        public InstallPlan()
        {
            Files = new List<FileTask>();
            Natives = new List<NativesExtraction>();
            PostDownload = new List<Func<CancellationToken, Task>>();
        }

        /// <summary>
        /// 依次下载文件、解压 natives、执行 PostDownload、生成虚拟 assets。
        /// 顺序不能换：natives 与加载器都依赖已经下载的游戏文件。
        /// </summary>
        public async Task<TransferStats> MaterializeAsync(DownloadEngine engine, IProgress<TransferStats> progress,
            CancellationToken ct)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (Files == null || Files.Count == 0) return new TransferStats();

            var stats = await engine.RunAsync(Files, progress, ct).ConfigureAwait(false);

            NativesUnpacker.ExtractAll(Natives, ct);

            foreach (var step in PostDownload)
            {
                ct.ThrowIfCancellationRequested();
                await step(ct).ConfigureAwait(false);
            }

            if (VirtualAssets || MapToResources) LinkVirtualAssets(ct);

            return stats;
        }

        /// <summary>旧版本按名字引用资源，只能复制一份扁平目录；这里不建符号链接，避免权限问题。</summary>
        private void LinkVirtualAssets(CancellationToken ct)
        {
            if (Assets?.Objects == null) return;

            var objectsRoot = Path.Combine(Root, "assets", "objects");
            var flatRoot = VirtualAssets
                ? Path.Combine(Root, "assets", "virtual", AssetIndexId)
                : Path.Combine(Root, "resources");

            foreach (var pair in Assets.Objects)
            {
                ct.ThrowIfCancellationRequested();

                var asset = pair.Value;
                if (asset?.Hash == null || asset.Hash.Length < 4) continue;

                var source = Path.Combine(objectsRoot, asset.Hash.Substring(0, 2), asset.Hash);
                if (!File.Exists(source)) continue;

                var relative = pair.Key.Replace('/', Path.DirectorySeparatorChar);
                var destination = Path.Combine(flatRoot, relative);

                var dir = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                if (File.Exists(destination)) continue;

                try
                {
                    File.Copy(source, destination);
                }
                catch (IOException)
                {
                    // 同一个资源被映射两次，忽略即可。
                }
            }
        }
    }
}
