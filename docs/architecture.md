# 架构

这篇讲 McDl 内部怎么分层、一次安装从点击到落盘经过哪些步骤，以及想加东西时该改哪里。命令和参数看 [cli.md](cli.md)，格式细节看 [packs.md](packs.md)，加载器看 [loaders.md](loaders.md)。

## 分层

```
src\McDl.App (WinForms)  src\McDl.Cli (mcdl.exe)
            \              /
             \            /
          src\McDl.Core
```

两个前端都只依赖 `McDl.Core`，不共享代码。Core 内部按职责分目录：

| 目录 | 主要类型 | 负责 |
| --- | --- | --- |
| `Api` | `Installer`、`IInstallSource`、`InstallRequest`、`InstallPlan`、`InstallSummary`、`InstallContext` | 对外门面，把请求路由到来源，再执行计划 |
| `Sources` | `VanillaSource`、`ModpackSource`、`FabricLoader` | 把一次请求解析成计划；原版与整合包各一个来源 |
| `Engine` | `DownloadEngine`、`MirrorRouter`、`FileTask`、`TransferStats` | 下载、分块续传、SHA1 校验、镜像排序；不认识 Minecraft 元数据 |
| `Minecraft` | `VanillaResolver`、`Models`、`NativesUnpacker` | 把版本 JSON、资源索引变成文件清单；解压 natives |
| `Packs` | `IPackFormat`、`PackFormats`、`PackManifest`、四个格式读取器 | 识别整合包格式，统一成 `PackManifest` |
| `Loaders` | `ILoaderInstaller`、`LoaderInstallers`、`FabricInstaller`、`ForgeInstaller`、`JavaLocator`、`LoaderSpec` | 在原版计划上补加载器；Forge 还要跑官方 installer |
| `Support` | `Json`、`PlatformInfo`、`RuleEvaluator`、`Text` | 序列化、平台判断、规则匹配、文案出口 |

依赖方向基本是向下的：`Api` 知道 `Sources` 和 `Engine`，`Sources` 知道 `Minecraft`、`Packs`、`Loaders`，`Engine` 和 `Support` 不知道别的层。`Minecraft` 会用到 `Engine` 的 `FileTask` 与镜像分类，但不知道有几种安装来源。

一个刻意的约束：Core 不引用 `System.Windows.Forms`，前端也不直接调 `DownloadEngine`。所有安装都走 `Installer`，这样 CLI 和 GUI 的行为不会分叉。

## 一次安装的时序

以 `Installer.InstallAsync(request, progress, ct, onResolved)` 为例：

1. **Resolve**：`Installer.ResolveAsync` 按 `request.Kind` 从 `_sources` 里取 `VanillaSource` 或 `ModpackSource`，把 `InstallContext`（引擎、平台、日志回调）传进去。
   - 原版：拉 `version_manifest_v2.json`，按版本 id 或 `latest` / `latest-snapshot` 选中条目，再拉该版本的 JSON。`VanillaSource.ResolveAsync` 这时就会写 `versions\<id>\<id>.json`；要 assets 的话，还会写 `assets\indexes\<index>.json`。
   - 整合包：`PackFormats.Read` 识别格式并读出 `PackManifest`，再用包里的游戏版本走一遍原版解析，最后交给对应的 `ILoaderInstaller`。`pack.Loader.IsSupported` 为 false 时在解析阶段就抛错。
   - 顺便说清楚：解析阶段不是纯内存操作。原版版本 JSON、资源索引、纯原版包和 Fabric 包的启动配置都会在这一步落盘；Forge / NeoForge 的启动配置要等 installer 跑完才能写。
2. **onResolved**：计划确定、还没开始传文件时回调一次。前端拿它显示文件数和总体积；CLI 打印 `label (version), N files, X MB`。
3. **Materialize**：`InstallPlan.MaterializeAsync` 依次做四件事。
   1. `DownloadEngine.RunAsync`：按 `TargetPath` 去重，起 N 个工作线程，每个文件最多试 3 次。目标已存在且 SHA1 匹配就计入“已存在”；否则写 `.part`，支持 Range 续传，校验通过后 `File.Move` 覆盖目标。
   2. `NativesUnpacker.ExtractAll`：先清空目标 natives 目录（避免换架构后留着旧 dll），再把压缩包里的文件按文件名平铺进去，跳过 `META-INF/`、`*.sha1`、`*.md` 等。
   3. `PostDownload`：按注册顺序执行。目前只有 Forge / NeoForge 用它——等游戏库文件到位后，用本机 Java 跑 `java -jar installer.jar --installClient <root>`。
   4. `LinkVirtualAssets`：只有 `VirtualAssets` 或 `MapToResources` 为 true 时执行，把 `assets\objects\<前两位>\<hash>` 复制成 `assets\virtual\<index>\<名字>` 或 `resources\<名字>`。函数名叫 Link，实际做的是复制。
4. **汇总**：`InstallSummary` 带 `Plan`、`TransferStats`、`Failures` 和 `Error`。单文件失败会汇总成 `IOException`，`Installer` 捕获后写进 `Error`，同时把每个失败文件的第一条错误放进 `Failures`。`InstallSummary.Succeeded` 要求没有 `Error`、有统计、失败数为 0。

下载阶段的失败不会回滚已经下好的文件。这是有意的：重跑时 SHA1 命中就跳过，续传成本最低。

## 为什么这样切

- **引擎不认识 Minecraft**。`DownloadEngine` 只接受 `FileTask`（URL、目标路径、SHA1、大小、镜像类别），所以换协议、加资源类型都不碰下载逻辑。
- **计划是数据**。`InstallPlan` 把“要下什么、要解什么、下完还要做什么”摊开，CLI 的 `plan` 命令能直接打印，测试也能在断网情况下断言文件数。
- **来源可替换**。原版和整合包的差异全在 `IInstallSource` 后面；`Installer` 只做路由。
- **加载器是加法**。加载器不重写原版解析，只在已有计划上追加库文件、`PostDownload` 和启动配置。
- **前端很薄**。GUI 和 CLI 都只创建 `Installer`、传 `InstallRequest`、收 `IProgress<TransferStats>`。

## 扩展点

**加一种安装来源**：实现 `IInstallSource`，在 `Installer` 构造函数的 `_sources` 字典里登记，并在 `InstallKind` 里加枚举值。注意 `ResolveAsync` 拿到的是共享的 `InstallContext`，下载元数据要走 `context.Engine`，这样才会享受镜像回退。

**加一种整合包格式**：实现 `IPackFormat`（`Id`、`DisplayName`、`CanRead`、`Read`），在 `PackFormats.All` 里登记。`CanRead` 只能读 zip 目录信息，不要解压整包；`Read` 返回统一的 `PackManifest`，认不出来时抛 `InvalidDataException`。顺序上，索引文件名不同的格式不会互相抢；HMCL 的 `modpack.json` 还要检查 `manifest.type`。

**加一种加载器**：实现 `ILoaderInstaller`，加进 `LoaderInstallers.Instances`；`LoaderSpec.FromKey` 要能识别包里的写法，`LoaderSpec.IsSupported` 要放行。解析阶段能确定的东西直接加进 `plan.Files`；依赖游戏文件到位的动作放进 `plan.PostDownload`。现有 Fabric 和 Forge 是两个极端，照着改就行。

**加一个前端**：只引用 `McDl.Core`，用 `Installer` 的 `ListVersionsAsync`、`ResolveAsync`、`InstallAsync`，日志接 `Installer.Log`，进度接 `IProgress<TransferStats>`。不要绕过 `Installer` 直接 new `DownloadEngine`，那会把镜像和失败处理甩给前端。

**加一个镜像**：在 `MirrorProfile` 里配置 meta / maven / assets 三个基地址，`MirrorRouter.Classify` 能认出的 host 才会参与改写。直链（`SourceKind.Other`）不会走镜像。

## i18n 的现状

`Support\Text.cs` 里有 `Text.Get(key)`、`ITextCatalog` 和 `ChineseCatalog`，目前只有 5 条中文条目，覆盖“缺 Java”“不支持的加载器”“无法识别整合包”等。`Text.Catalog` 可以在启动时替换成别的实现，这是给以后留的接口。

但要说清楚：**这不等于已经支持多语言**。存量文案（GUI 的标签、CLI 的输出、部分异常消息）还是硬编码中文，`Text` 目前也没有调用点。新代码应该优先走 `Text.Get`，旧代码等实际需要时再迁。

## 几个容易踩的点

- `FileTask.Kind` 必须是正确的 `SourceKind`，否则镜像会按错的策略排序。库文件用 `Maven`，资源用 `Assets`，版本元数据和资源索引都是 `Meta`。
- `InstallPlan.MaterializeAsync` 在 `Files` 为空时直接返回，`PostDownload` 也不会跑。纯加载器、无文件下载的计划不要依赖这个分支。
- 解析阶段已经写了磁盘文件。`plan` 命令不是只读的。
- `Json` 读配置时关掉了日期解析（`DateParseHandling.None`），否则 `1.21` 这种版本号会被 Newtonsoft 当成日期。
- 同一个 `Installer` 同时只跑一次安装。GUI 在安装期间会禁用开始按钮；自己写前端时也要串行化。
