# McDl

一个独立的 **Minecraft 原版文件下载器**：把某个游戏版本跑起来所需要的全部文件（客户端 jar、libraries、natives、资源包 resources、资源索引、版本 JSON）下载并校验好，得到一个可以直接交给启动器使用的 `.minecraft` 目录。

图形界面（WinForms，单文件 exe）+ 命令行两种入口，共用同一个自研下载引擎。

> 本项目与 PCL、PCL2、HMCL 等第三方启动器没有任何关联，也不包含它们的代码。它只做一件事：**按 Mojang 公开的版本元数据，从官方（或社区镜像）地址下载文件并校验**。

## 特性

- **版本列表**：打开即拉取全量版本（约 920 个），支持 `全部 / 正式版 / 快照` 过滤与搜索
- **并行下载**：默认 8 线程可调，`HttpClient` + 连接复用
- **完整性校验**：每个文件下载后校验 SHA1，已存在且校验通过的文件直接跳过
- **断点续传**：下载落盘到 `.part` 临时文件，中断/取消后从已下载字节继续
- **失败重试**：单文件最多 3 次、指数退避；持续失败自动降级到镜像
- **镜像回退**：官方优先，连续失败后按惩罚权重排序切换到 BMCLAPI，任意文件可混用
- **老版本兼容**：支持 `legacy`/`pre-1.6` 的虚拟资源树展开（1.6.x 及更早的 resources 目录结构）
- **natives 解压**：同时支持旧式 `natives` 映射（classifiers）与新式 `:natives-windows` 命名，自动去掉 `META-INF`
- **可取消**：安装过程中「取消」即时生效，已下载内容保留，下次继续

## 快速开始

发布包只有一个 `McDl.exe`（内置依赖），双击运行即可：

1. 左侧选择版本（默认选中最新正式版）
2. 右侧确认安装目录、线程数，是否需要资源文件（assets）
3. 点 **开始安装**，进度、速度、当前文件、日志都会实时更新

`.NET Framework 4.8` 为 Windows 10/11 内置运行时，无需额外安装。

## 命令行

```powershell
# 列出最新版本
McDl.Cli.exe list --limit 20

# 预览某版本需要下载什么（不写入磁盘）
McDl.Cli.exe plan 26.3

# 安装到指定目录
McDl.Cli.exe install 26.3 --root "D:\Games\mc-26.3" --threads 16

# 只要客户端本体，不要资源文件
McDl.Cli.exe install 1.6.4 --root "D:\Games\mc-1.6.4" --no-assets
```

## 构建

```powershell
# 只需要 .NET SDK 用于编译；运行环境是 .NET Framework 4.8
dotnet build mcdl.sln -c Release
```

产物：`src\McDl.App\bin\Release\net48\McDl.exe`（Costura 打包，单文件约 370 KB）。

运行单元测试：

```powershell
pwsh -File tools\test.ps1
```

打发布包：

```powershell
pwsh -File tools\build-release.ps1
```

## 项目结构

```
src/McDl.Core/            下载与解析逻辑（不依赖 WinForms）
  Api/                    对外门面：Installer / InstallRequest / InstallSummary
  Sources/                安装来源：VanillaSource / ModpackSource / FabricLoader
  Engine/                 DownloadEngine（并发、校验、续传）、MirrorRouter（镜像降权）
  Minecraft/              版本 JSON 模型、VanillaResolver、NativesUnpacker
  Support/                Json、PlatformInfo
src/McDl.App/             WinForms 图形界面
src/McDl.Cli/             命令行入口
tests/McDl.Tests/         单元测试（xUnit）
tools/                    发布、测试、截图与 UI 自动化脚本
```

分层、扩展点与设计取舍详见 [ARCHITECTURE.md](ARCHITECTURE.md)。

## 设计说明

**镜像策略**：`MirrorRouter` 持有官方与 BMCLAPI 两套地址改写规则。每次下载失败会给该镜像记一次惩罚（指数增长、有上限），成功则衰减；排序时按「惩罚权重 + 轮转」挑一个可用地址，因此官方慢/挂掉时能自动切换到镜像，恢复后又优先回到官方。

**为什么自己解析版本 JSON**：目标环境是裸 `.NET Framework 4.8`，不想为解析再拖入额外依赖。`Support/Json.cs` 只封装了 Newtonsoft.Json 的读写与日期处理（`DateParseHandling.None`，避免版本号里形如 `1.21` 的字符串被误判成日期），序列化模型集中在 `Minecraft/Models.cs`。

**整合包范围**：`.mrpack` 只安装**游戏本体 + 对应 loader 加载器**，不含模组/资源包等其它文件——这部分由启动器负责。

**断点续传的边界**：`.part` 文件只按已下载字节续传，不校验部分内容；只有完整文件才会落成正式名字并做 SHA1 校验。校验失败的文件会被视为不存在并重新下载。

## 免责声明

- 本项目只下载 **Mojang 官方发布**的文件或社区镜像上的同源副本，不提供、不分发任何游戏内容本身。
- 使用本项目即表示你自行承担与 Minecraft 最终用户许可协议（EULA）相关的一切责任；请通过正版账号游玩。
- 作者与 Mojang、Microsoft、BMCLAPI 及相关社区项目无从属关系。

## 许可证

本仓库代码以 MIT 协议发布，详见 [LICENSE](LICENSE)。
