# 更新日志

本文件格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [0.4.4] - 2026-10-23

### 修复

- 修复：Release 配置不再生成 PDB（`DebugType=none` / `DebugSymbols=false`）。0.4.3 解决了检出换行差异后，CI 与本机的 `McDl.Core.dll` 仍差 72 字节：PE 调试目录里写着 PDB 的 GUID 与校验和，而 portable PDB 的字节本身跨机器不稳定；Roslyn 又把调试信息算进模块版本号，于是内嵌它的单文件 exe 整块不同。发布产物本来就不带 PDB，Release 直接不产 PDB
- 添加：CI 日志打印 `McDl.Core.dll`、未内嵌 exe、发布 exe 各自的 SHA256，方便下次跨机器比对时直接定位到组件

## [0.4.3] - 2026-10-23

### 修复

- 修复：`.gitattributes` 改为统一按 LF 检出（仅 `*.sln`、`*.ps1`、`*.bat`、`*.cmd` 保留 CRLF）。C# 源码的行尾会进 PDB 的文档校验和，进而改变模块版本号：CI 检出 CRLF、本机工作树是 LF，同一提交算出的 `McDl.Core.dll` 就不同（0.4.2 实测：CI `12F9B609…`、本机 `6FBFC4D7…`），内嵌它的单文件 exe 随之整块不同
- 添加：`tools/build-release.ps1` 与 CI 增加行尾检查，编译输入里出现 CRLF 直接失败，避免再次悄悄产出无法复现的哈希
- 文档：`docs/reproducible-builds.md` 记录第三条根因（行尾 → PDB 文档校验和 → MVID），并给出 Windows 参与者的 `core.autocrlf false` 配置

## [0.4.2] - 2026-10-23

### 修复

- 修复：`PathMap` 增加 NuGet 全局缓存目录的映射（`$(NuGetPackageRoot)=/_/nuget/`）。引用程序集的绝对路径会进入编译器输入，本机 `C:\Users\Admin\.nuget` 与 CI 的 `C:\Users\runneradmin\.nuget` 让 `McDl.Core.dll` 的模块版本号不同（0.4.1 实测：CI `0787A95F…`、本机 `500F44BC…`），进而让内嵌了它的单文件 exe 整块不同
- 修复：`tools/build-release.ps1` 把发布包里的 `README.md` / `LICENSE.txt` 统一成 UTF-8 无 BOM + LF。`.gitattributes` 让 CI 检出的是 CRLF，直接复制会让 zip 随检出环境变化
- 文档：`docs/reproducible-builds.md` 记录这两条根因与验证方式

## [0.4.1] - 2026-10-23

### 修复

- 修复：`global.json` 的 `rollForward` 由 `latestPatch` 改为 `disable`。0.4.0 的本机构建用 SDK 10.0.301、CI 用 10.0.303，同一提交与命令产出的 zip 哈希不同（本机 `09760064…`、CI `B490F621…`）。钉死补丁版后两边使用同一编译器，产出一致
- 文档：`docs/reproducible-builds.md` 补上 SDK 补丁版约束的实测过程，`BUILDING.md` 写明必须安装精确的 10.0.301

## [0.4.0] - 2026-10-23

### 添加

- 添加：确定性构建——`Directory.Build.props` 打开 `Deterministic`、`PathMap`、SourceLink 与 `ContinuousIntegrationBuild`，同一提交在相同 SDK 下可产出逐字节一致的 `McDl.exe`（两个独立目录已实测；CI 端尚未跑过，未验证项见 `docs/reproducible-builds.md`）
- 添加：依赖锁定——`packages.lock.json` 覆盖四个项目，`dotnet restore --locked-mode` 按锁文件还原；根目录 `global.json` 固定 SDK 10.0.301
- 添加：发布附 `SHA256SUMS`、`McDl-<版本>.exe.sha256` 与 `BUILD-INFO.txt`（版本、提交、SDK、构建命令、嵌入的依赖）
- 添加：不打包依赖版 `McDl-<版本>-win-unbundled.zip`，与单文件版同时发布
- 添加：cosign keyless 签名（`SHA256SUMS.sig`、`SHA256SUMS.pem`）与 SBOM（`sbom.json`）
- 添加：Forge / NeoForge installer 按 maven 的 `.sha1` 校验；取不到 `.sha1` 时不中止，日志说明本次未校验
- 添加：`SECURITY.md`、`BUILDING.md`，以及 `docs/network-and-files.md`、`docs/verifying-releases.md`、`docs/reproducible-builds.md`

### 变更

- 变更：README 增补依赖表、验证发布包、网络与本地文件行为，构建一节改指 `BUILDING.md`
- 变更：发布流水线改为读取 `global.json` 安装 SDK，打 tag 后生成签名与 SBOM 再上传
- 变更：Fabric 库改为逐库请求上游 `.sha1`，拿到就强校验、拿不到则这个库不校验并在结束时统计；解析收敛到 `Support\Hashes.cs`，与 Forge installer 共用

## [0.3.0] - 2026-10-21

### 添加

- 添加：整合包格式扩展——CurseForge / HMCL 导出的 `manifest.json`、MCBBS 的 `mcbbs.packmeta`、HMCL 的 `modpack.json`，与原有的 Modrinth `.mrpack` 统一由 `PackFormats.Read` 探测
- 添加：Forge / NeoForge 自动安装——下载官方 installer，用本机 Java 执行 `--installClient`，并生成指向加载器版本的 profile
- 添加：Java 探测（`JAVA_HOME`、`PATH`、注册表、常见安装目录、Mojang runtime），按游戏元数据或版本兜底选择主版本
- 添加：`docs/` 文档体系（架构、整合包、加载器、命令行、测试、贡献），`tools/api-surface.ps1` 生成公开 API 一览
- 添加：GUI 与 CLI 按内容识别四种整合包格式，不再只看 `.mrpack` 扩展名
- 添加：GUI 支持把任意受支持的整合包拖进窗口或作为启动参数

### 变更

- 变更：Quilt 明确列为不支持，检测到会在解析阶段给出说明
- 变更：测试增至 115 个用例，覆盖整合包读取器、加载器版本拼接与 Java 探测
- 变更：根目录 `ARCHITECTURE.md` 并入 `docs/architecture.md`，`CONTRIBUTING.md` 改为指向 `docs/contributing.md` 的入口
- 变更：`mcdl plan` 也接受整合包路径，可先看整合包要下多少东西
- 变更：窗口标题从「Minecraft 原版下载器」改为「Minecraft 下载器」

## [0.2.1] - 2026-10-21

### 变更

- 变更：依赖升级——Newtonsoft.Json 13.0.4、Costura.Fody 6.2.0、Microsoft.NET.Test.Sdk 18.11.0、xunit.runner.visualstudio 4.0.1、Microsoft.Bcl.AsyncInterfaces 10.0.13（单文件打包行为与体积已回归验证）
- 变更：CI 的 job 名改为 `build`，便于在分支保护里勾选「必需状态检查」

## [0.2.0] - 2026-10-21

### 添加

- 添加：整合包游戏本体下载——读取 Modrinth `.mrpack` 索引，安装其声明的 Minecraft 版本与 Fabric 加载器，并生成 `inheritsFrom` 版本 profile
- 添加：`.mrpack` 可直接拖入窗口，或作为命令行参数启动 `McDl.exe`
- 添加：下载引擎 / 图形界面 / 命令行的分层重构，`McDl.Core` 不再依赖界面层（`Api / Sources / Engine / Minecraft / Support`）
- 添加：`mcdl.sln`、xUnit 测试项目（52 个用例）与 `ARCHITECTURE.md`（现已并入 `docs/architecture.md`）
- 添加：GitHub Actions（CI 构建 + 打 tag 自动发布）、Issue/PR 模板、Dependabot 配置

### 变更

- 变更：命令行与图形界面共用同一套下载与解析逻辑
- 变更：首次运行默认安装目录改为 `%APPDATA%\.minecraft`
- 变更：整合包安装完成后页脚显示整合包名而非原版版本号

## [0.1.0] - 2026-10-20

首个公开版本。

### 添加

- 添加：WinForms 图形界面，通过 Costura 打包为单文件 `McDl.exe`
- 添加：版本列表拉取与 `全部 / 正式版 / 快照` 过滤、关键字搜索
- 添加：并行下载（默认 8 线程可调）与逐文件 SHA1 校验，已存在且校验通过的文件自动跳过
- 添加：基于 `.part` 临时文件的断点续传，取消或中断后可从已下载字节继续
- 添加：单文件重试（指数退避）与镜像回退（官方优先，失败后按惩罚权重切换到 BMCLAPI）
- 添加：`legacy` / `pre-1.6` 虚拟资源树展开
- 添加：natives 解压，兼容旧式 classifiers 映射与新式 `:natives-windows` 命名，并过滤 `META-INF`
- 添加：命令行入口，支持 `list` / `plan` / `install` 子命令
