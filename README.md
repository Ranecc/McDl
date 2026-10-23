# McDl

McDl 是面向 Windows 的 Minecraft 游戏文件下载器。它把某个版本或某个整合包运行所需的文件——客户端 jar、libraries、natives、资源索引与 assets——写入指定的 `.minecraft` 目录，产出可被 HMCL、PCL2、官方启动器等直接使用的游戏本体。账号登录、版本隔离与游戏启动由启动器负责，不属于 McDl 的职责。

本项目的直接背景：2026 年 10 月 20 日，PCL2 自 2.99.0.0 起移除内置下载功能，同日国内数个第三方启动器亦相继下线下载服务。此后补一份干净的游戏文件，需要使用者自行获取版本清单、库文件、资源索引与各类镜像地址。McDl 将这一流程收敛为单个可执行文件。

## 支持范围

**游戏版本。** Mojang 公开发布的任意版本，含正式版、快照与早期版本。

**整合包格式。** Modrinth 的 `.mrpack`、CurseForge 与 HMCL 导出的 `manifest.json`、MCBBS 的 `mcbbs.packmeta`、HMCL 的 `modpack.json`，以及纯原版包。McDl 安装整合包的游戏本体与加载器；包内的模组、配置文件、资源包与光影不在下载范围内，其清单记录仅用于识别所需版本与加载器。

**加载器。** Fabric 的库列表取自官方 meta；Forge 与 NeoForge 下载官方 installer，并以本机 Java 执行 `--installClient`。Quilt 不受支持，检测到 Quilt 整合包时将明确告知，不做兼容性尝试。

**平台。** 本版本基于 WinForms 与 .NET Framework 4.8，仅支持 Windows 10、11；Linux 与 macOS 暂不在计划内。

## 图形界面

Windows 10/11 直接双击 `McDl.exe` 运行，无需预装 .NET。exe 约 365 KB，核心库与 JSON 库已嵌入其中。

界面左侧为可搜索的版本列表。选定版本后确认安装目录（首次打开默认为 `%APPDATA%\.minecraft`），决定是否一并下载 assets，点击「开始安装」。整合包可通过拖入窗口或点击「整合包…」安装；窗口将显示识别出的包格式、Minecraft 版本与加载器，详细步骤见日志面板。

assets 复选框记住上次选择，首次运行时默认不勾选：完整资源约占总下载量的九成，不勾选时仅下载客户端、库与 natives。不勾选不影响游戏本体安装，但游戏内将缺少声音与语言文件。

## 命令行

命令行版本位于 `src\McDl.Cli`，编译产物为 `mcdl.exe`，未包含在发布 zip 中，需自行构建：

```powershell
dotnet build src\McDl.Cli\McDl.Cli.csproj -c Release
.\src\McDl.Cli\bin\Release\net48\mcdl.exe list --limit 3
```

以下示例中的 `mcdl` 均指该 exe；如需直接使用，请将其所在目录加入 `PATH`。

```
versions: 920

26.4-snapshot-5    snapshot   2026-10-20T12:15:06+00:00
26.4-snapshot-4    snapshot   2026-10-13T12:05:46+00:00
26.4-snapshot-3    snapshot   2026-10-06T12:54:26+00:00
```

安装前可用 `plan` 查看将下载的内容规模：

```powershell
mcdl plan 1.20.1
```

```
name         : 1.20.1
version      : 1.20.1
asset index  : 5
root         : <安装目录>\.minecraft
files        : 3649
size         : 699.1 MB

  Assets     3598 files    621.5 MB
  Maven        50 files     55.6 MB
  Meta          1 files     22.0 MB

natives to unpack:
  lwjgl-glfw-3.3.1-natives-windows.jar
  ...
```

`plan` 只解析版本元数据并打印结果，不执行下载；但该过程会将版本 JSON 与资源索引写入目标目录，因此不应在正在使用的 `.minecraft` 上执行。

正式安装：

```powershell
mcdl install 1.20.1 --root D:\Games\.minecraft --threads 8
mcdl install latest-snapshot --no-assets
mcdl modpack .\某个整合包.mrpack --root D:\Games\.minecraft
```

`install` 先打印计划，随后以单行刷新的进度条显示过程，结束时汇总下载数、已存在数与失败数。过程中可按 Ctrl+C 取消；重新执行时，SHA1 校验通过的文件直接跳过，未完成的文件自 `.part` 续传。完整命令与参数见 [docs/cli.md](docs/cli.md)。

## 目录结构

以 `install --root D:\Games\.minecraft` 为例：

```
D:\Games\.minecraft\
  versions\1.20.1\1.20.1.json            版本配置
  versions\1.20.1\1.20.1.jar             客户端 jar
  libraries\...                          库文件，按 Maven 目录排布
  assets\indexes\5.json                  资源索引
  assets\objects\ab\abcdef...            资源本体，按 SHA1 前缀分目录
  assets\virtual\5\...                   1.7 以前版本的虚拟资源目录
  resources\...                          更早版本的扁平资源目录
  .mcdl\installers\...                   Forge / NeoForge 官方 installer
  versions\<整合包名>\<整合包名>.json     整合包装配后的启动配置，继承加载器版本
```

除上述内容外，游戏目录中不会产生其他文件。`.mcdl` 为 McDl 的工作目录，删除后不影响已安装版本，下次安装 Forge 时 installer 会重新下载。

图形界面把安装目录、线程数与 assets 勾选记在 `%APPDATA%\McDl\settings.json`，不写进游戏目录；命令行不读写这份设置。

## 运行环境

Windows 10 或 11。Win10 1903 起系统自带 .NET Framework 4.8，无需单独安装运行时。

Java 仅在安装 Forge、NeoForge 时需要；原版下载与 Fabric 安装不涉及 Java。所需 Java 版本优先取自 Mojang 版本元数据，元数据缺失时按下表兜底：

| Minecraft | Java |
| --- | --- |
| 1.20.5 及以后 | 21 |
| 1.18 – 1.20.4 | 17 |
| 1.17 | 16 |
| 1.16.5 及更早 | 8 |

McDl 依次在 `JAVA_HOME`、`PATH`、注册表 JavaSoft / Adoptium 项、`Program Files` 下的 `Java`、`Eclipse Adoptium`、`Microsoft` 子目录，以及官方启动器下发的 `runtime` 中查找 `java.exe`，并对每个候选实际执行 `java -version` 核验版本，而非仅依据路径判断。选择策略：优先主版本号与要求精确匹配者；无匹配时退而取高于要求中版本最低者——部分旧版 Forge installer 仅接受其对应时代的 Java。全部候选均不可用时，报错信息会指明所需版本，例如：

```
Forge 的 installer 需要 Java 17 或更高版本，但本机未找到可用的 java.exe。
可安装 JDK 17+，或将 java.exe 所在目录加入 PATH 后重试。
```

## 镜像与代理

每个下载请求带有类别标记：版本元数据（Meta）、Maven 库（Maven）、资源文件（Assets）、其他直链（Other）。Meta、Maven、Assets 优先请求官方源，失败后切换 BMCLAPI。某条线路失败一次，其在后续请求中的优先级即下降；成功一次后恢复。整合包内直链与 Fabric meta 属于 Other 类别，不走镜像。

程序不设独立代理配置，HTTP 请求遵循 Windows 系统代理。

## 验证发布包

每个 release 附 `SHA256SUMS`、`McDl-<版本>.exe.sha256`、`BUILD-INFO.txt` 与 `sbom.json`，`SHA256SUMS` 另有一份 cosign keyless 签名（`SHA256SUMS.sig` 与 `SHA256SUMS.pem`）。先验签，再对哈希，命令可直接复制，见 [docs/verifying-releases.md](docs/verifying-releases.md)。发现安全问题请走私密报告，方式见 [SECURITY.md](SECURITY.md)。

## 它会访问哪些地址、写哪些文件

只连 Minecraft 官方域名与 BMCLAPI 镜像，不访问 GitHub API，没有遥测，也没有自动更新检查。安装只写目标目录，图形界面的设置写在 `%APPDATA%\McDl\settings.json`，不写注册表，不落日志文件。完整域名清单、目录布局，以及 Forge / NeoForge installer 会自己联网这件事，见 [docs/network-and-files.md](docs/network-and-files.md)。

## 常见问题

**下载缓慢或连不上官方源。** 先观察日志中线路是否已切换至 BMCLAPI。两条线路均慢，一般为本机网络问题；启用代理时检查系统代理是否实际生效。Fabric meta 为直连，不受镜像影响。

**下载中断。** 重新执行安装即可。已完整文件按 SHA1 跳过，未完成文件自 `.part` 续传。

**单个文件校验失败。** 先重试；仍失败则删除该文件后重跑。命令行会报告首个失败文件及原因。SHA1 不符通常意味着链路上存在返回错误页面的代理或镜像。

**Forge / NeoForge installer 中途退出或超时。** installer 运行上限 10 分钟，超时强制结束。先查日志中 installer 的最后 20 行，常见原因为 Java 版本不符、目录无写权限、下载处理器网络受阻。修正后重跑，已下载文件不会重复下载。

**installer 自行退出，日志仅数行，`versions` 目录无新增。** Forge 官方 installer 内部直连 `launchermeta.mojang.com`、`libraries.minecraft.net` 等域名，不经过 McDl 的镜像体系。若这些域名在本机网络中被代理、hosts 或加速工具干扰，installer 会静默失败且退出码为 0。放通上述域名或启用系统代理后重试。McDl 会核验 `versions` 下是否实际生成目录，未生成即按失败上报。

**装完能否直接启动。** McDl 不承担启动职能。以 HMCL、PCL2 或官方启动器打开该 `.minecraft` 即可，Forge / NeoForge / Fabric 的版本配置已写入。整合包内模组需由启动器另行获取。

**exe 为何仅三百余 KB。** 目标框架为 net48，运行时使用系统自带组件；Core 与 Newtonsoft.Json 经 Costura 嵌入 exe。若改为自带运行时的 .NET 8 单文件发布，体积在数十 MB 量级。

**安装至 `Program Files` 失败。** 权限不足，改用普通用户目录，或以管理员身份运行。

**日志文件。** 图形界面日志仅保留当次运行，不写文件。需要留存输出时使用命令行，可重定向保存。

## 构建与测试

环境要求：Windows 10 / 11，.NET SDK 10.0.301（`global.json` 固定，`rollForward` 为 `latestPatch`），PowerShell 7。完整步骤见 [BUILDING.md](BUILDING.md)。

```powershell
dotnet restore mcdl.sln --locked-mode
dotnet build mcdl.sln -c Release
pwsh -NoProfile -File tools\test.ps1
pwsh -NoProfile -File tools\build-release.ps1 -Version 0.4.0
```

`tools\test.ps1` 经 xunit.console 直接运行 net48 测试程序集；勿用 `dotnet test`，缘由见 [docs/testing.md](docs/testing.md)。

发布脚本在 `dist\` 下产出两个包：`McDl-<版本>-win.zip` 是单文件版，`McDl-<版本>-win-unbundled.zip` 把 `McDl.Core.dll` 与 `Newtonsoft.Json.dll` 放在 exe 旁边。同时生成 `SHA256SUMS`、`BUILD-INFO.txt`、`RELEASE-NOTES.md`、`sbom.json`，以及单独的 `McDl-<版本>.exe` 与它的 `.sha256`。两个 zip 里都不放 PDB。

### 依赖

| 依赖 | 用途 |
| --- | --- |
| Newtonsoft.Json | 解析 Mojang 版本元数据、整合包与 Fabric meta 的 JSON |
| Costura.Fody | 构建时把托管依赖嵌进单文件 exe |
| Microsoft.SourceLink.GitHub | 把仓库地址与提交信息写进程序集和 PDB |
| xunit（含 xunit.runner.console） | 测试，由 `tools\test.ps1` 运行 |

具体版本以各项目旁的 `packages.lock.json` 为准，`dotnet restore --locked-mode` 按锁文件还原，不跟随浮动版本。

### 可复现构建

同一 SDK、同一提交，两个不同目录 clean build 得到的 `McDl.exe` 逐字节一致（已实测，打包脚本产出的 exe 与两个 zip 也一致）。CI 产物与本机的比对还没有结论，现状与验证方法见 [docs/reproducible-builds.md](docs/reproducible-builds.md)。

修改代码前请先阅读 [docs/contributing.md](docs/contributing.md)；内部分层见 [docs/architecture.md](docs/architecture.md)。

## License

MIT，见 [LICENSE](LICENSE)。

代码为本项目独立实现，行为依据 Mojang 公开协议文档、Modrinth / CurseForge / MCBBS / HMCL 的公开包格式说明及各加载器的官方 API。本项目以 MIT 发布，与采用其他许可的既有下载器之间不存在代码来源关系。
