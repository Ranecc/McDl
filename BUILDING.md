# 从源码构建

目标框架是 net48。SDK 只负责编译和跑脚本，编译出来的东西运行时不需要 SDK，也不需要 .NET 8 运行时。

## 需要什么

- Windows 10 / 11。`McDl.App` 是 WinForms 程序，测试与发布脚本也是 PowerShell。
- .NET SDK 10.0.301，必须精确这个版本。仓库根的 `global.json` 用 `rollForward: disable` 钉死，装 10.0.302 或更新的补丁版都不行——补丁版不同，编出来的字节就不同，见 [docs/reproducible-builds.md](docs/reproducible-builds.md)。版本不对时 `dotnet` 会直接报错并提示需要哪个版本，照提示装。`dotnet --version` 应输出 `10.0.301`。
- PowerShell 7（`pwsh`）。Windows PowerShell 5.1 大多也能跑，下文命令按 `pwsh` 写。
- 不需要 Visual Studio。net48 的引用程序集由 `Microsoft.NETFramework.ReferenceAssemblies` 包提供，有 SDK 就够。

## 还原、编译、测试

```powershell
git clone https://github.com/Ranecc/McDl.git
cd McDl

dotnet restore mcdl.sln --locked-mode
dotnet build mcdl.sln -c Release
pwsh -NoProfile -File tools\test.ps1
```

`--locked-mode` 要求还原结果和每个项目旁边的 `packages.lock.json` 完全一致，依赖树有任何漂移都会直接失败。改过依赖（加包、升版本）后，先重新生成锁文件，再把 `packages.lock.json` 与项目文件一起提交：

```powershell
dotnet restore mcdl.sln --force-evaluate
```

四个项目：

| 项目 | 输出 | 说明 |
| --- | --- | --- |
| `src\McDl.Core` | `McDl.Core.dll` | 下载引擎、镜像路由、版本解析、加载器安装 |
| `src\McDl.App` | `McDl.exe` | WinForms 图形界面，`AssemblyName` 为 `McDl` |
| `src\McDl.Cli` | `mcdl.exe` | 命令行 |
| `tests\McDl.Tests` | `McDl.Tests.dll` | xUnit 测试 |

测试脚本没有用 `dotnet test`。原因在 `.NET 8+ / 10 SDK`：随 SDK 下发的 vstest ObjectModel 在 net48 测试宿主里加载不了 xUnit 2.x 适配器（缺 `Microsoft.Bcl.AsyncInterfaces`），discovery 会报 “no tests available”。脚本的做法是先 `dotnet build` 测试项目，再从 NuGet 全局包目录找到 `xunit.runner.console\2.9.3\tools\net48\xunit.console.exe`，直接跑 `McDl.Tests.dll`，退出码与测试结果一致。更多背景见 [docs/testing.md](docs/testing.md)。

## 打发布包

```powershell
pwsh -NoProfile -File tools\build-release.ps1 -Version 0.4.0
```

产物落在 `dist\`（已在 `.gitignore` 中）：

| 文件 | 内容 |
| --- | --- |
| `McDl-0.4.0-win.zip` | 单文件版。`McDl.Core.dll` 与 `Newtonsoft.Json.dll` 已由 Costura 嵌进 `McDl.exe`，旁边只放 `README.md` 与 `LICENSE.txt` |
| `McDl-0.4.0-win-unbundled.zip` | 不打包依赖版。`McDl.exe` + `McDl.Core.dll` + `Newtonsoft.Json.dll` + `README.md` + `LICENSE.txt` |
| `McDl-0.4.0.exe` | 单文件版里的那个 exe，单独拷出来方便比对哈希 |
| `McDl-0.4.0.exe.sha256` | 单独一行 `<hash>  McDl.exe` |
| `SHA256SUMS` | 两个 zip 的 SHA-256，标准 `<hash>  <文件名>` 格式（两个空格）。不包含它自己 |
| `BUILD-INFO.txt` | 版本、git describe、完整 commit、工作树是否干净、`dotnet --version`、配置与操作系统、三条完整构建命令、Costura 内嵌依赖、两个 zip 与 exe 的 SHA256、不内嵌版文件清单 |
| `RELEASE-NOTES.md` | Release 正文，带提交、构建命令、两个 zip 的哈希与完整变更链接；同时作为资产上传 |
| `sbom.json` | CycloneDX 1.5，由脚本从四份 `packages.lock.json` 生成（直接依赖 + 传递依赖，带 SHA-512；同一版本与 commit 得到同一个序列号） |

两个 zip 里都没有 PDB。

`-Version` 是唯一的版本来源：脚本把它作为 `-p:Version`、`-p:FileVersion`、`-p:AssemblyVersion` 传给构建，所以 zip 文件名、exe 右键属性里的版本、`BUILD-INFO.txt`、`sbom.json` 里的版本号一致，不需要另外改 `McDl.App.csproj`。

两个包功能相同，区别只在托管依赖放哪：

- 单文件版适合普通使用，解压即双击，不怕删错 dll。
- 不打包依赖版适合要审计、要用杀软扫、或者想自己替换 `Newtonsoft.Json.dll` 的场景。这个包用 `-p:DisableFody=true` 构建，Fody 不介入，dll 就散在 exe 旁边。

Costura 只是构建期工具。`Costura.dll` 不会进发布包；exe 运行时从自己的资源里 `Assembly.Load` 嵌入的 dll，不联网下载任何 dll。这也是 exe 只有三百多 KB 的原因：net48 组件由系统提供。

上面这些在本地一次跑完。release 流水线（`.github\workflows\release.yml`）做的是同一件事，再加一步：用 cosign keyless 给 `SHA256SUMS` 签名，产出 `SHA256SUMS.sig` 与 `SHA256SUMS.pem`，然后建 GitHub Release。签名要在 GitHub Actions 上跑，本地需要 OIDC 身份，这里不展开。校验方式见 [docs/verifying-releases.md](docs/verifying-releases.md)。

## 自己验证可复现性

同一 SDK、同一提交，在两个不同目录里删掉 `bin` / `obj` 再构建，`McDl.exe` 是逐字节一致的（已实测）。自己验：

```powershell
dotnet clean mcdl.sln -c Release
dotnet build mcdl.sln -c Release
(Get-FileHash src\McDl.App\bin\Release\net48\McDl.exe -Algorithm SHA256).Hash
(Get-Item src\McDl.App\bin\Release\net48\McDl.exe).VersionInfo.ProductVersion
```

把哈希记下来，换一个目录重来一遍，比较两个数。

要对着发布包验，就得照抄那次 `BUILD-INFO.txt` 里的三条命令。发布脚本传了 `-p:Version=0.4.0 -p:FileVersion=0.4.0.0 -p:AssemblyVersion=0.4.0.0`，得到的 exe 与上面裸 build 的不是同一份字节——两种都稳定，但不相等。另外 zip 里放着 `README.md` 与 `LICENSE.txt`，这两份文件一改，zip 哈希就变，exe 不变。

`ProductVersion` 里带着提交号，可以用来确认两边是不是同一份源码。哪些场景已验证、哪些还没有，写在 [docs/reproducible-builds.md](docs/reproducible-builds.md)。

## 更新 API 一览

改了 `McDl.Core` 的公开 API 后重跑：

```powershell
pwsh -NoProfile -File tools\api-surface.ps1
```

脚本从 XML 文档注释生成 `docs/api-surface.md`，内容没变化时不重写文件。

## 常见问题

**`dotnet build` 报 NU1004 或锁文件不一致。** 项目文件与 `packages.lock.json` 漂移了。先跑 `dotnet restore mcdl.sln --force-evaluate`，检查生成的锁文件差异再提交。

**`dotnet --version` 不是 10.0.301。** `global.json` 现在是 `rollForward: disable`，只认 10.0.301，任何别的版本（哪怕同一 band 的更高补丁）都会让 SDK 解析失败并提示需要哪个版本。按提示装 10.0.301，或者改 `global.json` 并承担产物对不上的风险。

**杀软把 `McDl.exe` 拦了。** Costura 会在运行时解压并加载嵌入式程序集，行为上像自解压程序，部分杀软会误报。换成不打包依赖版通常可以绕开。

**`dist\` 里的 zip 里没有 `mcdl.exe`。** 命令行版本来就不进发布包，需要时自己构建 `src\McDl.Cli`。
