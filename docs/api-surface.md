# McDl.Core API 一览

> 由脚本生成，勿手工编辑。生成命令：`pwsh -NoProfile -File tools/api-surface.ps1`
>
> 只列 `public` 类型与成员，说明取自源码里的 XML 注释；没有注释的留空。目录是 `src/McDl.Core`。

共 252 行（类型行 + 成员行）。

| 命名空间 | 类型 | 成员 | 说明 |
| --- | --- | --- | --- |
| McDl.Core | IInstallSource | (interface) | 一种可安装的东西（原版版本、整合包……）。 |
| McDl.Core | InstallContext | (class) | 解析请求时交给 IInstallSource 的共享服务。 |
| McDl.Core | InstallContext | public InstallContext(DownloadEngine engine, PlatformInfo platform) : this(engine, platform, null) |  |
| McDl.Core | InstallContext | public InstallContext(DownloadEngine engine, PlatformInfo platform, Action<string> log) |  |
| McDl.Core | InstallContext | public DownloadEngine Engine | 网络访问，带镜像回退；来源不要自己造。 |
| McDl.Core | InstallContext | public PlatformInfo Platform | 平台规则，用来筛库。 |
| McDl.Core | InstallContext | public Action<string> Log | 给用户看的进度文字，可以为 null。 |
| McDl.Core | Installer | (class) | 安装流程的唯一入口：持有 DownloadEngine，按 InstallKind 选来源并执行计划。 |
| McDl.Core | Installer | public Installer(MirrorRouter router = null, int threads = 8, string userAgent = null) |  |
| McDl.Core | Installer | public IReadOnlyList<string> Failures | 上次安装失败的文件及首个错误。 |
| McDl.Core | Installer | public Action<string> Log | 前端接日志的地方；null 表示不输出。 |
| McDl.Core | Installer | public Task<VersionManifest> ListVersionsAsync(CancellationToken ct) | 官方版本清单（所有版本 + latest 指针）。 |
| McDl.Core | Installer | public Task<InstallPlan> ResolveAsync(InstallRequest request, CancellationToken ct) | 只解析要下载什么，不下载 payload。 |
| McDl.Core | Installer | public async Task<InstallSummary> InstallAsync(InstallRequest request, IProgress<TransferStats> progress, CancellationToken ct, Action<InstallPlan> onResolved = null) | 解析并下载，进度通过 progress 报告。 |
| McDl.Core | Installer | public void Dispose() |  |
| McDl.Core | InstallPlan | (class) | 一次安装解析出来的全部内容：下载什么、解压什么、下载后还要做什么。 |
| McDl.Core | InstallPlan | public string Root | 目标 .minecraft 目录。 |
| McDl.Core | InstallPlan | public string VersionId | 接收下载内容的版本目录名。 |
| McDl.Core | InstallPlan | public string Label | 界面上显示的名字（版本号、整合包名等）。 |
| McDl.Core | InstallPlan | public string AssetIndexId | assets/indexes 下的索引 id；旧版本是 "legacy"。 |
| McDl.Core | InstallPlan | public VersionDetail Detail | 解析后的版本 JSON；不基于原版元数据的计划为 null。 |
| McDl.Core | InstallPlan | public AssetIndex Assets | 解析后的资源索引；不下载 assets 时为 null。 |
| McDl.Core | InstallPlan | public List<FileTask> Files | 这次安装要落到磁盘的全部文件。 |
| McDl.Core | InstallPlan | public List<NativesExtraction> Natives | 下载完成后要解压的 natives 包。 |
| McDl.Core | InstallPlan | public bool VirtualAssets | 旧版本按名字引用资源，需要 assets/virtual/<id> 这种扁平布局。 |
| McDl.Core | InstallPlan | public bool MapToResources | 资源索引出现之前的版本读扁平的 resources 目录。 |
| McDl.Core | InstallPlan | public List<Func<CancellationToken, Task>> PostDownload | 下载完成之后才做的工作，比如跑 Forge 官方 installer。 |
| McDl.Core | InstallPlan | public long TotalBytes |  |
| McDl.Core | InstallPlan | public InstallPlan() |  |
| McDl.Core | InstallPlan | public async Task<TransferStats> MaterializeAsync(DownloadEngine engine, IProgress<TransferStats> progress, CancellationToken ct) | 依次下载文件、解压 natives、执行 PostDownload、生成虚拟 assets。 |
| McDl.Core | InstallKind | (enum) | 用户要装的东西的类型。 |
| McDl.Core | InstallRequest | (class) | 一次安装请求，安装流水线唯一的输入。 |
| McDl.Core | InstallRequest | public InstallKind Kind |  |
| McDl.Core | InstallRequest | public string Root | 目标 .minecraft 目录；相对路径按工作目录算。 |
| McDl.Core | InstallRequest | public string Version | 原版版本号，或 "latest" / "latest-snapshot"。 |
| McDl.Core | InstallRequest | public string ModpackPath | 整合包路径，格式交给 PackFormats 判断。 |
| McDl.Core | InstallRequest | public bool IncludeAssets | 是否下载资源索引及 assets 对象。 |
| McDl.Core | InstallRequest | public InstallRequest Clone() |  |
| McDl.Core | InstallRequest | public string ResolveRoot() |  |
| McDl.Core | InstallSummary | (class) | 一次安装的结果，CLI 和 UI 都按这个结构展示。 |
| McDl.Core | InstallSummary | public InstallPlan Plan |  |
| McDl.Core | InstallSummary | public TransferStats Stats | 传输统计；下载开始前就失败时为 null。 |
| McDl.Core | InstallSummary | public IReadOnlyList<string> Failures | 没能落盘的文件及首个错误。 |
| McDl.Core | InstallSummary | public string Error | 整体中断的原因（不是单个文件失败时）。 |
| McDl.Core | InstallSummary | public bool Succeeded |  |
| McDl.Core | FileTask | (class) | 一个待落盘文件：从哪来、到哪去、怎么校验。 |
| McDl.Core | FileTask | public string Label |  |
| McDl.Core | FileTask | public string Url |  |
| McDl.Core | FileTask | public string TargetPath |  |
| McDl.Core | FileTask | public string Sha1 |  |
| McDl.Core | FileTask | public long Size |  |
| McDl.Core | FileTask | public SourceKind Kind |  |
| McDl.Core | FileTask | public bool Optional |  |
| McDl.Core | TransferStats | (class) | 一次传输的快照，进度回调和最终结果共用。 |
| McDl.Core | TransferStats | public int TotalFiles |  |
| McDl.Core | TransferStats | public int DoneFiles |  |
| McDl.Core | TransferStats | public int SkippedFiles |  |
| McDl.Core | TransferStats | public int FailedFiles |  |
| McDl.Core | TransferStats | public long TotalBytes |  |
| McDl.Core | TransferStats | public long DoneBytes |  |
| McDl.Core | TransferStats | public double BytesPerSecond |  |
| McDl.Core | TransferStats | public string CurrentFile |  |
| McDl.Core | TransferStats | public string LastError |  |
| McDl.Core | TransferStats | public double Percent | 没有字节数（老版本或空计划）时退回按文件数算。 |
| McDl.Core | DownloadEngine | (class) | 并发下载器：固定线程数、.part 断点续传、SHA1 校验、镜像回退。 |
| McDl.Core | DownloadEngine | public const string DefaultUserAgent = "mcdl/0.1 (+https://github.com/)" |  |
| McDl.Core | DownloadEngine | public DownloadEngine(MirrorRouter router, int parallelism = 8, string userAgent = null) |  |
| McDl.Core | DownloadEngine | public IReadOnlyList<string> Failures | 本次运行失败的文件及错误，重跑前不会自动清空。 |
| McDl.Core | DownloadEngine | public async Task<string> GetTextAsync(string url, SourceKind kind, CancellationToken ct) | 下载一段文本（元数据）；按候选镜像依次重试，全部失败才抛。 |
| McDl.Core | DownloadEngine | public async Task<TransferStats> RunAsync(IEnumerable<FileTask> tasks, IProgress<TransferStats> progress, CancellationToken ct) | 跑完整个文件列表。 |
| McDl.Core | DownloadEngine | public static bool HashMatches(string path, string sha1) | SHA1 为空时只判断文件存在；用于没有哈希的上游。 |
| McDl.Core | DownloadEngine | public void Dispose() |  |
| McDl.Core | SourceKind | (enum) | URL 属于哪类上游；分类是为了能分别换到镜像上。 |
| McDl.Core | SourceCandidate | (class) | 一个可尝试的下载地址，以及它来自哪个镜像配置。 |
| McDl.Core | SourceCandidate | public string Url |  |
| McDl.Core | SourceCandidate | public string Profile |  |
| McDl.Core | SourceCandidate | public SourceKind Kind |  |
| McDl.Core | MirrorProfile | (class) | 一个镜像的基地址；Official 为 true 时不改 URL，直接走官方。 |
| McDl.Core | MirrorProfile | public string Name |  |
| McDl.Core | MirrorProfile | public bool Official |  |
| McDl.Core | MirrorProfile | public string MetaBase |  |
| McDl.Core | MirrorProfile | public string MavenBase |  |
| McDl.Core | MirrorProfile | public string AssetsBase |  |
| McDl.Core | MirrorRouter | (class) | 把官方 URL 改写到镜像上，并按分类记录失败次数；下次请求优先用还活着的镜像。 |
| McDl.Core | MirrorRouter | public MirrorRouter() |  |
| McDl.Core | MirrorRouter | public MirrorRouter Add(MirrorProfile profile) | 追加一个镜像；顺序只是初始顺序，实际排序看惩罚分。 |
| McDl.Core | MirrorRouter | public IReadOnlyList<MirrorProfile> Profiles |  |
| McDl.Core | MirrorRouter | public static MirrorRouter CreateDefault() | 默认配置：官方 + BMCLAPI。 |
| McDl.Core | MirrorRouter | public static SourceKind Classify(string url) | 按 host 判断 URL 类型；不认识的 host（Modrinth、Fabric meta 等）归为 Other，不镜像。 |
| McDl.Core | MirrorRouter | public IReadOnlyList<SourceCandidate> Candidates(string url, SourceKind kind) | 按当前惩罚分列出候选地址；Other 类只有原始地址。 |
| McDl.Core | MirrorRouter | public void NoteSuccess(string profile, SourceKind kind) | 成功一次就把该镜像的清零，让它重新排到前面。 |
| McDl.Core | MirrorRouter | public void NoteFailure(string profile, SourceKind kind) | 失败一次加一分，只影响同类 URL 的排序。 |
| McDl.Core | FabricInstaller | (class) | Fabric：官方 meta 直接给出启动所需库列表，不需要跑任何本地程序。 |
| McDl.Core | FabricInstaller | public LoaderKind Kind |  |
| McDl.Core | FabricInstaller | public string DisplayName |  |
| McDl.Core | FabricInstaller | public async Task ApplyAsync(InstallContext context, InstallPlan plan, string gameVersion, LoaderSpec loader, string profileId, CancellationToken ct) |  |
| McDl.Core | ForgeInstaller | (class) | Forge 与 NeoForge：两者都提供官方 installer jar，内部自带处理器。 |
| McDl.Core | ForgeInstaller | public ForgeInstaller(LoaderKind kind) |  |
| McDl.Core | ForgeInstaller | public LoaderKind Kind |  |
| McDl.Core | ForgeInstaller | public string DisplayName |  |
| McDl.Core | ForgeInstaller | public Task ApplyAsync(InstallContext context, InstallPlan plan, string gameVersion, LoaderSpec loader, string profileId, CancellationToken ct) |  |
| McDl.Core | ILoaderInstaller | (interface) | 一种加载器的安装方式。 |
| McDl.Core | LoaderInstallers | (class) | 加载器实现的分发点。 |
| McDl.Core | LoaderInstallers | public static ILoaderInstaller For(LoaderKind kind) |  |
| McDl.Core | LoaderInstallers | public static bool IsSupported(LoaderKind kind) |  |
| McDl.Core | JavaRuntime | (class) | 本机上的一个 Java 运行时。 |
| McDl.Core | JavaRuntime | public JavaRuntime(string path, string version, int majorVersion) |  |
| McDl.Core | JavaRuntime | public string Path | java.exe 的完整路径。 |
| McDl.Core | JavaRuntime | public string Version | 完整版本字符串，例如 "17.0.12"。 |
| McDl.Core | JavaRuntime | public int MajorVersion | 主版本号，例如 8 / 17 / 21。 |
| McDl.Core | JavaLocator | (class) | 找本机可用的 Java。 |
| McDl.Core | JavaLocator | public static IReadOnlyList<JavaRuntime> Discover() | 列出找到的所有 Java，按主版本号从高到低。 |
| McDl.Core | JavaLocator | public static IReadOnlyList<JavaRuntime> Discover(IEnumerable<string> extraRoots) | 找出本机所有 Java，按主版本号从高到低排列。 |
| McDl.Core | JavaLocator | public static JavaRuntime Find(int requiredMajor) | 挑一个主版本号不低于 requiredMajor 的 Java；找不到返回 null。 |
| McDl.Core | LoaderKind | (enum) | 本项目认识的加载器。 |
| McDl.Core | LoaderSpec | (class) | 整合包声明的加载器及版本。 |
| McDl.Core | LoaderSpec | public static readonly LoaderSpec None = new LoaderSpec(LoaderKind.None, null) |  |
| McDl.Core | LoaderSpec | public LoaderSpec(LoaderKind kind, string version) |  |
| McDl.Core | LoaderSpec | public LoaderKind Kind |  |
| McDl.Core | LoaderSpec | public string Version | 包声明的版本；为空表示“用最新稳定版”。 |
| McDl.Core | LoaderSpec | public bool IsSupported | 能不能由 McDl 自己装。 |
| McDl.Core | LoaderSpec | public string DisplayName |  |
| McDl.Core | LoaderSpec | public string UnsupportedMessage | 装不了的时候给用户看的一句话。 |
| McDl.Core | LoaderSpec | public static LoaderSpec FromKey(string key, string version) | 把各格式的依赖键（modrinth / curseforge 的写法）翻成本枚举。 |
| McDl.Core | VersionManifest | (class) |  |
| McDl.Core | LatestVersions | (class) |  |
| McDl.Core | VersionEntry | (class) |  |
| McDl.Core | VersionDetail | (class) |  |
| McDl.Core | JavaVersionInfo | (class) |  |
| McDl.Core | DownloadEntry | (class) |  |
| McDl.Core | AssetIndexRef | (class) |  |
| McDl.Core | AssetIndex | (class) |  |
| McDl.Core | AssetObject | (class) |  |
| McDl.Core | LibraryEntry | (class) |  |
| McDl.Core | LibraryDownloads | (class) |  |
| McDl.Core | ExtractRule | (class) |  |
| McDl.Core | RuleEntry | (class) |  |
| McDl.Core | OsRule | (class) |  |
| McDl.Core | NativesUnpacker | (class) | 把 natives 包解到版本目录，按版本 JSON 的 extract 规则过滤。 |
| McDl.Core | NativesUnpacker | public static readonly List<string> DefaultExcludes = new List<string> | 默认排除项：压缩包自带的记账文件，不是游戏需要的东西。 |
| McDl.Core | NativesUnpacker | public static void ExtractAll(IEnumerable<NativesExtraction> extractions, CancellationToken ct) |  |
| McDl.Core | NativesExtraction | (class) | 一个需要解压到版本目录的 natives 包。 |
| McDl.Core | NativesExtraction | public FileTask Archive |  |
| McDl.Core | NativesExtraction | public string TargetDir |  |
| McDl.Core | NativesExtraction | public List<string> Excludes |  |
| McDl.Core | VanillaResolver | (class) | 把原版版本 JSON 拆成普通文件列表。 |
| McDl.Core | VanillaResolver | public const string MetaBase = "https://piston-meta.mojang.com" |  |
| McDl.Core | VanillaResolver | public const string ManifestUrl = MetaBase + "/mc/game/version_manifest_v2.json" |  |
| McDl.Core | VanillaResolver | public const string AssetObjectBase = "https://resources.download.minecraft.net" |  |
| McDl.Core | VanillaResolver | public static string MavenRelativePath(string coordinate) |  |
| McDl.Core | VanillaResolver | public static string NativeClassifier(LibraryEntry library, PlatformInfo platform) |  |
| McDl.Core | VanillaResolver | public static List<FileTask> LibraryFiles(VersionDetail detail, string root, PlatformInfo platform, List<NativesExtraction> natives) |  |
| McDl.Core | VanillaResolver | public static List<FileTask> ClientFiles(VersionDetail detail, string root) |  |
| McDl.Core | VanillaResolver | public static List<FileTask> AssetFiles(AssetIndex index, string root) |  |
| McDl.Core | VanillaResolver | public static long TotalBytes(IEnumerable<FileTask> tasks) |  |
| McDl.Core | CurseForgePackFormat | (class) | CurseForge 与 HMCL 导出的整合包：索引是 manifest.json。 |
| McDl.Core | CurseForgePackFormat | public const string IndexEntryName = "manifest.json" |  |
| McDl.Core | CurseForgePackFormat | public string Id |  |
| McDl.Core | CurseForgePackFormat | public string DisplayName |  |
| McDl.Core | CurseForgePackFormat | public bool CanRead(string archivePath) |  |
| McDl.Core | CurseForgePackFormat | public PackManifest Read(string archivePath) |  |
| McDl.Core | HmclPackFormat | (class) | HMCL 自己的整合包格式：索引是 modpack.json。 |
| McDl.Core | HmclPackFormat | public const string IndexEntryName = "modpack.json" |  |
| McDl.Core | HmclPackFormat | public string Id |  |
| McDl.Core | HmclPackFormat | public string DisplayName |  |
| McDl.Core | HmclPackFormat | public bool CanRead(string archivePath) |  |
| McDl.Core | HmclPackFormat | public PackManifest Read(string archivePath) |  |
| McDl.Core | IPackFormat | (interface) | 一种整合包格式的读取器。 |
| McDl.Core | PackFormats | (class) | 格式登记表：按顺序试，第一个认领的格式负责解析。 |
| McDl.Core | PackFormats | public static readonly IReadOnlyList<IPackFormat> All = new IPackFormat[] | 注意顺序：modrinth 与 curseforge 的索引文件名不一样，谁都不会抢谁的。 |
| McDl.Core | PackFormats | public static PackManifest Read(string archivePath) |  |
| McDl.Core | McbbsPackFormat | (class) | MCBBS 格式：索引是 mcbbs.packmeta，国内老包常见。 |
| McDl.Core | McbbsPackFormat | public const string IndexEntryName = "mcbbs.packmeta" |  |
| McDl.Core | McbbsPackFormat | public string Id |  |
| McDl.Core | McbbsPackFormat | public string DisplayName |  |
| McDl.Core | McbbsPackFormat | public bool CanRead(string archivePath) |  |
| McDl.Core | McbbsPackFormat | public PackManifest Read(string archivePath) |  |
| McDl.Core | ModpackManifest | (class) | The "modrinth.index.json" document inside a .mrpack archive. |
| McDl.Core | ModpackManifest | public string MinecraftVersion | Minecraft version the pack is built on; also accepts the legacy "minecraft" key only. |
| McDl.Core | ModpackManifest | public string FabricLoaderVersion |  |
| McDl.Core | ModpackManifest | public string UnsupportedLoader | Name of a loader this project does not install yet, or null. |
| McDl.Core | ModpackManifest | public string DisplayName |  |
| McDl.Core | ModpackManifest | public string BuildProfileId() | Turns the pack name into a folder safe version id ("微光 1.2" -> "微光-1.2"). |
| McDl.Core | ModpackFile | (class) | One entry of the pack's file list; only the game body is installed today, the rest is metadata. |
| McDl.Core | ModpackFile | public string Sha1 |  |
| McDl.Core | ModpackFileEnvironment | (class) |  |
| McDl.Core | ModpackArchive | (class) | Reads the pack index out of a .mrpack archive without unpacking the whole file. |
| McDl.Core | ModpackArchive | public const string IndexEntryName = "modrinth.index.json" |  |
| McDl.Core | ModpackArchive | public static ModpackManifest ReadIndex(string archivePath) |  |
| McDl.Core | ModrinthPackFormat | (class) | Modrinth 的 .mrpack：索引是 modrinth.index.json。 |
| McDl.Core | ModrinthPackFormat | public const string IndexEntryName = "modrinth.index.json" |  |
| McDl.Core | ModrinthPackFormat | public string Id |  |
| McDl.Core | ModrinthPackFormat | public string DisplayName |  |
| McDl.Core | ModrinthPackFormat | public bool CanRead(string archivePath) |  |
| McDl.Core | ModrinthPackFormat | public PackManifest Read(string archivePath) |  |
| McDl.Core | PackManifest | (class) | 整合包读出来之后的统一形状：不管是 Modrinth、CurseForge、MCBBS 还是 HMCL， 上层只看这一个对象。 |
| McDl.Core | PackManifest | public string FormatId | 识别出来的格式 id，见 PackFormats。 |
| McDl.Core | PackManifest | public string Name | 整合包名，可能为空。 |
| McDl.Core | PackManifest | public string VersionId | 整合包自己的版本号，可能为空。 |
| McDl.Core | PackManifest | public string MinecraftVersion | 对应的原版版本，必填；为空说明这个包没写清楚。 |
| McDl.Core | PackManifest | public LoaderSpec Loader | 加载器；纯原版包是 LoaderSpec.None。 |
| McDl.Core | PackManifest | public List<PackFileEntry> Files | 包里的文件清单（模组、配置等）。 |
| McDl.Core | PackManifest | public string SourcePath | 整合包来源，绝对路径。 |
| McDl.Core | PackManifest | public PackManifest() |  |
| McDl.Core | PackManifest | public string DisplayName |  |
| McDl.Core | PackManifest | public string BuildProfileId() | 把包名变成可以当文件夹名的版本 id：“微光 1.2” → “微光-1.2”。 |
| McDl.Core | PackManifest | public long TotalSize | 把若干文件累加成一个总体积，给 UI 显示用。 |
| McDl.Core | PackFileEntry | (class) | 包里的一条文件记录。 |
| McDl.Core | PackFileEntry | public string Path | 相对整合包根目录的路径。 |
| McDl.Core | PackFileEntry | public string Sha1 | SHA1，可能为空。 |
| McDl.Core | PackFileEntry | public List<string> Downloads | 下载直链，可能为空（老格式只给 CurseForge project/file id）。 |
| McDl.Core | PackFileEntry | public long Size | 文件大小，未知时是 0。 |
| McDl.Core | PackFileEntry | public PackFileEntry() |  |
| McDl.Core | FabricLoaderMeta | (class) | Fabric loader metadata as published by meta.fabricmc.net. |
| McDl.Core | FabricLoaderMeta | public string MainClassClient |  |
| McDl.Core | FabricLoaderMeta | public List<FabricLibrary> ClientLibraries() | Loader libraries that have to sit in libraries/ for the client to start. |
| McDl.Core | FabricLoaderVersion | (class) |  |
| McDl.Core | FabricLauncherMeta | (class) |  |
| McDl.Core | FabricLibraries | (class) |  |
| McDl.Core | FabricLibrary | (class) |  |
| McDl.Core | FabricMainClass | (class) |  |
| McDl.Core | FabricLoader | (class) | Fetches Fabric loader metadata and folds it into an existing vanilla plan. |
| McDl.Core | FabricLoader | public const string MetaBase = "https://meta.fabricmc.net" |  |
| McDl.Core | FabricLoader | public static async Task<FabricLoaderMeta> FetchAsync(DownloadEngine engine, string gameVersion, string loaderVersion, CancellationToken ct) |  |
| McDl.Core | FabricLoader | public static void Apply(InstallPlan plan, FabricLoaderMeta meta, string profileId) | Adds the loader jars to the plan and writes a merged version profile next to the vanilla one, so launchers that understand "inheritsFrom"... |
| McDl.Core | ModpackSource | (class) | 整合包安装：先按格式读出包信息，再装对应的原版本体，最后把加载器挂上去。 |
| McDl.Core | ModpackSource | public string Kind |  |
| McDl.Core | ModpackSource | public async Task<InstallPlan> ResolveAsync(InstallContext context, InstallRequest request, CancellationToken ct) |  |
| McDl.Core | VanillaSource | (class) | Resolves versions published in the official version manifest, including the historical shapes of the version document (natives map, embed... |
| McDl.Core | VanillaSource | public string Kind |  |
| McDl.Core | VanillaSource | public static async Task<VersionManifest> FetchManifestAsync(DownloadEngine engine, CancellationToken ct) | Downloads and parses the version manifest; shared by the CLI and the UI. |
| McDl.Core | VanillaSource | public async Task<InstallPlan> ResolveAsync(InstallContext context, InstallRequest request, CancellationToken ct) |  |
| McDl.Core | VanillaSource | public async Task<InstallPlan> ResolveAsync(InstallContext context, string root, VersionEntry entry, bool includeAssets, CancellationToken ct) | Resolves a concrete manifest entry; also used by other sources that embed vanilla. |
| McDl.Core | VanillaSource | public static VersionEntry PickVersion(VersionManifest manifest, string spec) | Exact id wins over the "latest" aliases; null when nothing matches. |
| McDl.Core | Json | (class) | 统一的 JSON 读写，保证所有模型用同一套序列化设置。 |
| McDl.Core | Json | public static T Parse<T>(string text) where T : class |  |
| McDl.Core | Json | public static T Load<T>(string path) where T : class |  |
| McDl.Core | Json | public static void Save(string path, object value) |  |
| McDl.Core | PlatformInfo | (class) | 当前主机信息，同时作为 rules 判定的输入。 |
| McDl.Core | PlatformInfo | public string OsName |  |
| McDl.Core | PlatformInfo | public string OsVersion |  |
| McDl.Core | PlatformInfo | public string Arch |  |
| McDl.Core | PlatformInfo | public static PlatformInfo Current() |  |
| McDl.Core | PlatformInfo | public string ArchBits | 老版本 natives 名里用 32/64，不是 x86/x64。 |
| McDl.Core | RuleEvaluator | (class) | 版本清单里的 allow/disallow 规则；没有规则视为允许，最后一条匹配的规则生效。 |
| McDl.Core | RuleEvaluator | public static bool IsAllowed(IList<RuleEntry> rules, PlatformInfo platform, IDictionary<string, bool> features) |  |
| McDl.Core | Text | (class) | 面向用户的文案出口。 |
| McDl.Core | Text | public static ITextCatalog Catalog |  |
| McDl.Core | Text | public static string Get(string key, params object[] args) |  |
| McDl.Core | ITextCatalog | (interface) |  |
| McDl.Core | ChineseCatalog | (class) |  |
| McDl.Core | ChineseCatalog | public string Format(string key, params object[] args) |  |
