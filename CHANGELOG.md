# 更新日志

本文件格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

0.x 是开发期版本：API、界面与行为都还在变，不承诺兼容。0.6.0 是 0.x 最后一个带二进制发行的版本，之后的 0.x 只留 tag。哈希、签名与可复现构建的承诺从 1.0.0 开始建立。

## [未发布]

### 变更

- 变更：0.6.0 是 0.x 最后一个带二进制发行的版本；之后到 1.0.0 之前只留 tag，不再产生 Release（`release.yml` 仍认 `v*`，由发版人自行决定打不打 tag）
- 变更：文档与日志里的本机路径统一改成占位符（`<安装目录>`、`<用户名>`）；CI 增加一道机器路径检查，用户目录与 UNC 路径进仓库即失败

## [0.6.0] - 2026-10-24

### 添加

- 添加：界面重写为三页——原版下载（版本列表、搜索、正式版/快照过滤）、整合包（选择或拖入，显示格式、Minecraft 版本与加载器）、设置（配色、默认目录、线程、assets）
- 添加：浅色 / 深色 / 跟随系统三档配色；跟随系统时读 `AppsUseLightTheme`，读不到按浅色
- 添加：按钮与卡片的投影、悬停发光、按下 1px 位移；窗口圆角与云母背景，DWM 不支持时逐级降级
- 添加：系统 DPI 感知（`app.manifest` + `Metrics.Scale`），控件尺寸与字号跟随缩放；`MCDL_UI_SCALE` 可强制比例（调试用）
- 添加：`.mrpack` / `.zip` / `.hmcl` 拖入窗口，命令行参数可直接打开整合包
- 文档：新增 `docs/ui.md`（页面结构、设计 token、主题与 DPI、自绘控件的坑）；`docs/architecture.md` 补 App 层

### 变更

- 变更：安装面板固定为右栏；「校验文件」在「打开目录」左边，装完自动复核后可手动复查，校验可取消并复用进度条
- 变更：设置文件新增 `ThemeMode`；旧 `settings.json` 缺这一项时按「跟随系统」

### 修复

- 修复：深色主题下原生日志框仍是浅色底
- 修复：主按钮文字对比度不足（浅色 3.80:1、深色 2.32:1），改用 `OnAccent` 前景色
- 修复：禁用态在按钮、输入框、勾选框、数字框上都能看出来；按钮与勾选框补键盘焦点提示
- 修复：自绘文字原本没有抗锯齿（`TextRenderer` 画到双缓冲位图上是硬边）；改走 GDI+ `DrawString` + ClearType，`MCDL_TEXT_HINT=gray` 可切灰度抗锯齿
- 修复：125% / 150% 缩放下部分文字整行消失、标题与副标题挤在一起——安装面板的间距、字号矩形与外壳高度统一改由 `Metrics` 换算
- 修复：手动校验的进度条在第一个文件后就走满（`UiProgress.Value` 是 0..1，回调却按 0..1000 写入）

## [0.5.0] - 2026-10-23

### 添加

- 添加：`mcdl verify` 子命令——只解析计划、不下载，按大小 + SHA1 复核磁盘上已安装的文件，列出缺失与不符的项；只报告不删除，退出码 0 / 1 / 2
- 添加：图形界面「校验文件」按钮，解析出计划后可用，可随时手动复查
- 添加：每次安装结束后自动复核一遍落盘文件（`InstallSummary.Verification`），防杀毒软件隔离、磁盘损坏与事后被改；失败只提示、文件保留
- 文档：`docs/verification.md` 说明校验范围、触发时机、失败处理与「为什么校验不能替代签名」；`docs/cli.md` 补 `verify` 用法与退出码表；`docs/testing.md` 补 `InstallVerifierTests` 与手工验收步骤；`docs/loaders.md` 与 `docs/verifying-releases.md` 补交叉引用与覆盖边界
- 文档：README 新增「免责声明」，界定工具属性（正版辅助）、不做授权校验因而要求用户自律、责任归属、透明可验证与无担保；FAQ「为什么不登录、不做正版校验」扩写为四段（不需要身份 / Azure 应用与客户端条款的持续义务 / 凭证代码带来的安全面 / 正版校验无实际意义）

### 变更

- 变更：README 徽章组（Powered by 🐳大肥鱼 / License / .NET Framework 4.8 / platform / CI / release / downloads）
- 变更：`SECURITY.md` 增加边界声明：不接收、不保存账号凭证，也不请求微软 / Mojang 授权

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

- 修复：`PathMap` 增加 NuGet 全局缓存目录的映射（`$(NuGetPackageRoot)=/_/nuget/`）。引用程序集的绝对路径会进入编译器输入，本机与 CI 的 NuGet 缓存目录（各自在自己的用户目录下）让 `McDl.Core.dll` 的模块版本号不同（0.4.1 实测：CI `0787A95F…`、本机 `500F44BC…`），进而让内嵌了它的单文件 exe 整块不同
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
