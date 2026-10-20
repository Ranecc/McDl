# 架构说明

本文面向后续维护者和贡献者，说明 McDl 的分层、扩展点和几条不可破坏的约束。

## 分层总览

```
src/
  McDl.Core/            纯逻辑，无 UI 依赖，net48 类库
    Api/                对外门面：Installer / InstallRequest / InstallSummary
    Sources/            安装来源实现：VanillaSource / ModpackSource / FabricLoader
    Engine/             下载与镜像：DownloadEngine / MirrorRouter
    Minecraft/          版本元数据解析：Models / VanillaResolver / NativesUnpacker
    Support/            横切工具：Json / PlatformInfo
  McDl.Cli/             命令行入口（mcdl list / plan / install …）
  McDl.App/             WinForms 图形界面（单文件 exe）
tests/
  McDl.Tests/           单元测试（xUnit，net48）
```

依赖方向是单向的：`App → Core ← Cli`，`Core` 内部的依赖方向为 `Api → Sources → Engine/Minecraft → Support`。

## 一次安装的流程

1. `InstallRequest` 描述「装什么、装到哪」（`Kind` 决定来源，`VersionSpec` 为版本或整合包路径）。
2. `Installer.CreatePlanAsync(request, progress, ct)` 选出一个 `IInstallSource` 并让它解析出 `InstallPlan`。
   - `InstallPlan` 只包含**纯数据**：`Files`（`FileTask` 列表）、`Natives`（需要解压的本地库）、`TotalBytes` 等，不产生任何磁盘副作用。
3. `Installer.InstallAsync(request, progress, ct)` 复用同一个 plan 对象，交给 `DownloadEngine` 下载后再调用 `MaterializeAsync` 落地（解压 natives、生成虚拟资源目录）。
4. `InstallSummary` 回传下载/跳过/失败计数，CLI 和 GUI 都只依赖这三个类型，不直接接触引擎。

这样做的目的是：**解析与下载分离**，`plan` 可以用来做预览、体积统计、测试断言，而下载阶段只关心 `FileTask`。

## 扩展点：新增一个安装来源

实现 `IInstallSource` 即可，无需改动引擎：

```csharp
public interface IInstallSource
{
    InstallKind Kind { get; }
    Task<InstallPlan> ResolveAsync(InstallRequest request, IProgress<TransferStats> progress, CancellationToken ct);
}
```

然后在 `Installer` 的构造/注册处把新实现挂进字典。现有两个实现是很好的模板：

- `VanillaSource`：拉版本清单 → 取版本 JSON → 解析库/资源/客户端。
- `ModpackSource`：读 `.mrpack` 的 `modrinth.index.json`，取出 `dependencies.minecraft`，**复用** `VanillaSource` 装游戏本体，再把整合包自己的 loader 版本装上去。

## 关键设计决策

| 决策 | 原因 |
| --- | --- |
| net48 + WinForms | Win10/11 自带 .NET Framework 4.8，产物约 400 KB，免安装；.NET 8 自包含包约 65 MB，不适合作为下载器分发。 |
| Costura.Fody 内嵌依赖 | net48 不支持 single-file 发布，用 Costura 把 `McDl.Core.dll`、`Newtonsoft.Json.dll` 作为资源嵌入，得到真正的单 exe。 |
| 只参考公开协议，不移植第三方启动器代码 | 第三方启动器使用自定义许可，禁止二次分发；本项目的元数据格式来自 Mojang 公开 JSON 与 Modrinth 公开规范。 |
| 镜像用「惩罚排序」而不是硬编码首选 | 国内直连 Mojang 经常失败、BMCLAPI 时快时慢，按失败次数动态降级比固定顺序更稳。 |
| 下载前用 SHA1 判定「已存在」 | 幂等：重复执行不会重复下载，断点续传靠 `.part` 文件。 |

## 不可破坏的约束

- `McDl.Core` 不得引用 `System.Windows.Forms`；UI 逻辑只允许出现在 `McDl.App`。
- `InstallPlan.ResolveAsync` 必须保持无副作用（不写磁盘），写盘只发生在 `MaterializeAsync` 或 `DownloadEngine`。
- 新增文件类型时，`FileTask.Kind` 要给出正确的 `SourceKind`，否则镜像回退策略会选错上游。
- 面向用户的中文字符串使用 UTF-8 无 BOM，`Directory.Build.props` 已设置 `<CodePage>65001</CodePage>`。

## 测试

```powershell
./tools/test.ps1
```

测试覆盖规则求值、maven 坐标解析、natives 分类器识别、镜像排序、`.mrpack` 解析与 natives 解压。

> 注意：本项目**不使用** `dotnet test`。新版 .NET SDK 的 vstest 在 net48 测试宿主中无法加载 xUnit 适配器（缺少 `Microsoft.Bcl.AsyncInterfaces`），会报「没有可用测试」。`tools/test.ps1` 直接调用 `xunit.console.exe`，CI 也走同一条路径。

## 发布

```powershell
./tools/build-release.ps1 -Version 0.2.0
```

产物在 `dist/`：`McDl.exe` + `README.md` + `LICENSE.txt` 打包为 zip，并输出 SHA256。
