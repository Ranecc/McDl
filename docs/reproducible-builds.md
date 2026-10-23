# 可复现构建

结论先说：同一提交、同一个 SDK、两个不同目录，删掉 `bin` / `obj` 后用同一套命令构建，得到的 `McDl.exe` 逐字节一致。打包脚本产出的 exe 与两个 zip 也一致，**GitHub Actions 上构建的发布产物同样一致**——v0.4.4 起已经逐字节对上（见下表与「实测数字」）。

但「换个环境还能一致」要额外满足四条，每一条都是踩过才补上的：SDK 补丁版一字不差（v0.4.0）、NuGet 缓存路径也要进 `PathMap`（v0.4.1）、发布包里的文本文件行尾要归一（v0.4.1–v0.4.3）、Release 配置不生成 PDB（v0.4.4，Costura 会把 PDB 的绝对路径写回 PE 头）。

## 现状

| 场景 | 状态 |
| --- | --- |
| 同一 SDK、同一提交、同一目录，两次 clean build | 已实测：`McDl.exe` 的 SHA-256 相同 |
| 同一 SDK、同一提交，两个不同目录（各自删 `bin` / `obj`） | 已实测：`McDl.exe` 逐字节一致 |
| `tools\build-release.ps1` 在两个目录的产物（exe 与两个 zip） | 已实测：全部一致 |
| GitHub Actions 产物与本机产物 | v0.4.0 不一致（SDK 补丁版不同）；v0.4.1 不一致（NuGet 缓存路径 + 行尾）；这两个根因在 v0.4.2 修好。**v0.4.4 已实测一致**：本机按 tag `v0.4.4` 重建的 `McDl-0.4.4-win.zip` 是 `37872E8C…`（368,540 B）、`-unbundled.zip` 是 `B597AA33…`（364,297 B），与该版本 Release 上 CI 产物的哈希、大小逐字节相同 |
| 不同 SDK 补丁版（10.0.301 与同 band 更高补丁，如 10.0.303） | 已实测：**不一致**，两个 zip 的哈希与大小都不同。见「SDK 补丁版必须一致」 |
| 换一套构建命令（例如不带 `-p:Version`） | 得出的 exe 与发布包不同，但换命令后自身稳定。见「为什么不承诺任意命令行」 |

## 实测数字

2026-10-23，SDK 10.0.301，`E:\DEV\WechatBotWorkspace\mcdl` 与副本目录，两边都先删 `bin` / `obj` / `dist`：

```text
dotnet build mcdl.sln -c Release
  McDl.exe  28C3A377143CCF0DF2012677128B0905C063D0017AC73AE29A63AB3C4B2EE33D  385,024 B
  mcdl.exe  836A3B05B0D25CAC7ABCD08AD77AD6D0531C490021D8A95226E9E816AD74455F   22,528 B

pwsh -NoProfile -File tools\build-release.ps1 -Version 0.4.0
  McDl-0.4.0.exe                36D3203BF6C0C06CBA80EA7220DDCEE7A4E660D7E95028365E118F6E08ED3E56
  McDl-0.4.0-win.zip            0153AC283F84E8C107EEEF4BBF882D2FA81778D49FEC259A57FD5B1E8BA2E36D
  McDl-0.4.0-win-unbundled.zip  6D0CC3BC127ED89FDD00F0F7006D1685C4BB2EC11A2F4E23D9ECCF49EE44D3E3
```

两个目录的结果完全相同。注意这组数字的来源：它出自一次**未提交**的工作树（commit `ca504c9`，`BUILD-INFO.txt` 里标了 dirty），证明的是「同一条命令在不同目录得到相同字节」这个性质，**不是任何 release 的哈希**。release 的权威哈希只在发布包附带的 `BUILD-INFO.txt` 与 `McDl-<版本>.exe.sha256` 里。

还有一点容易误会：exe 内嵌了 commit SHA（`ProductVersion` 形如 `0.4.0+<commit>`），所以不同提交打出来的 exe 字节必然不同。这是预期的，不违反可复现性——可复现性说的是「同一提交 + 同一 SDK + 同一命令 → 同一字节」。

## CI 与本机对齐（v0.4.4）

这不是纸上结论。2026-10-23 按 tag `v0.4.4`（提交 `8d99aeb7`）在另一个工作树里重建，与本机原来那份产物、以及 GitHub Actions 上传的发布包三方对比：

```text
McDl-0.4.4-win.zip            37872E8CEB4A8C1FEBC035373A637A773EDEA3DD8B9D0446532EA03283EDF9D2  368,540 B
McDl-0.4.4-win-unbundled.zip  B597AA33452EC400EE9D03E1A0648DD3FA537AA460981F3569A0006296C1D30F  364,297 B
McDl-0.4.4.exe                2B7B486351C02B402C52D721CB2B11918DD4AA3DE13BB82A74C88AF2E9E2AB1F  385,024 B
```

本机重建与 Release 页面上的 `SHA256SUMS` 逐字节相同，大小也相同。也就是说：只要你用 `global.json` 指定的 SDK、跑 `BUILD-INFO.txt` 里抄下来的那几条命令，就能得到与发布包完全一样的字节。

下面几节记录的是**对齐之前**踩过的每一层坑，读它们是为了知道为什么现在的构建参数长这样，而不是为了复现历史失败。

## SDK 补丁版必须一致

v0.4.0 发布后对过一次账，结论很直接：SDK 补丁版差一位，字节就对不上。

同一个提交 `4d632e2`、同一套命令，本机（SDK 10.0.301）和 GitHub Actions（`BUILD-INFO.txt` 里记的是 **10.0.303**）打出来的两个 zip 哈希都不同：

```text
McDl-0.4.0-win.zip
  本机  09760064F947CE498A3C0C23356E15BF693E53D247002C5B743C015D96F65421  368,527 B
  CI    B490F6215F72AC2D3935A8A9989675B1F5D3EC9E26AC2B36969556EF2B4D5779  368,556 B

McDl-0.4.0-win-unbundled.zip
  本机  1224B4F4D159F8097CEF51BF93827378E987B783CA5C8304DC9A23794000BF74
  CI    005C3232293FB5D7F5D2E82BB6230DE64B2DE82C0B46B31CF682AA9207861531
```

连大小都不一样（差 29 字节），说明不是打包容器的抖动，而是编译器产出了不同的二进制。

原因是当时的 `global.json` 写的是 `rollForward: latestPatch`：本机装的是 10.0.301，CI runner 上装的是 10.0.x 这条 band 里更新的 10.0.303。两个补丁版的编译器行为不同，输出版本号、元数据排布都可能变，字节自然不同。

现在 `global.json` 改成了 `"rollForward": "disable"`：只认 `10.0.301` 这精确的一个版本，本机低了高了都直接报错，不会悄悄换一个补丁版去构建。

给复现的人一句话：**装 `global.json` 指定的精确补丁版，不要用「10.0.x 最新」。** `rollForward: disable` 下版本不对，`dotnet` 会直接告诉你需要哪个版本，照提示装就行。装完 `dotnet --version` 应该一字不差。

## 路径不只仓库目录，还有 NuGet 缓存

v0.4.1 把 SDK 钉在 10.0.301 之后又对了一次账，**还是不一致**。这次两边 SDK 完全相同、工作树干净，所以问题不在版本号。

```text
McDl-0.4.1-win.zip     本机 F67FA74B…  368,626 B    CI D1CF795E…  368,665 B
McDl-0.4.1.exe         本机 69F6F1FC…  385,024 B    CI 29D4AA24…  385,024 B
```

exe 大小一样、哈希不同。于是拆开「不内嵌版」逐个文件比：

```text
McDl.exe              两边相同
Newtonsoft.Json.dll   两边相同
McDl.Core.dll         本机 500F44BC…   CI 0787A95F…   都是 107,008 B
```

`Core.dll` 逐字节比对只有 72 处不同：PE 校验和，加一处 16 字节的模块版本号（MVID）。MVID 由编译器按输入算出，输入里含引用程序集的绝对路径——本机 NuGet 全局缓存在 `C:\Users\Admin\.nuget\packages`，runner 上是 `C:\Users\runneradmin\.nuget\packages`。`PathMap` 当时只映射了仓库根目录，没映射 NuGet 缓存，于是 MVID 不同、`Core.dll` 不同，Costura 再把 `Core.dll` 压缩内嵌进 exe，压缩流整块变化（exe 里约 49,747 字节不同）。

修法是在 `Directory.Build.props` 的 `PathMap` 里追加 NuGet 缓存根：

```xml
<PathMap>$(MSBuildThisFileDirectory)=/_/</PathMap>
<PathMap Condition="'$(NuGetPackageRoot)' != ''">$(PathMap),$(NuGetPackageRoot)=/_/nuget/</PathMap>
```

**怎么在自己机器上验证这一条。** 准备两个全局包目录，各装一份依赖：

```powershell
$env:NUGET_PACKAGES = 'E:\tmp\nuget-a'
dotnet restore mcdl.sln --locked-mode
dotnet build src\McDl.Core\McDl.Core.csproj -c Release -t:Rebuild
(Get-FileHash src\McDl.Core\bin\Release\net48\McDl.Core.dll -Algorithm SHA256).Hash

$env:NUGET_PACKAGES = 'E:\tmp\nuget-b'
dotnet restore mcdl.sln --locked-mode
dotnet build src\McDl.Core\McDl.Core.csproj -c Release -t:Rebuild
(Get-FileHash src\McDl.Core\bin\Release\net48\McDl.Core.dll -Algorithm SHA256).Hash
```

修之前两个目录算出的哈希不同（`500F44BC…` 与 `BFBB747D…`）；映射了 `$(NuGetPackageRoot)` 之后两个目录都是 `BFBB747D…`。本地就能复现和验证，不用等 CI。

## 发布包里的文本文件，行尾也要归一

第二条根因不在编译，在打包：zip 里还放 `README.md` 与 `LICENSE.txt`。

`.gitattributes` 让 CI 检出出 CRLF 的这两份文件，本地工作树是 LF。`README.md` 本机 11,975 B、CI 12,165 B——每行多一个 `\r`。文件内容一样，字节不一样，zip 哈希自然不一样。

修法是 `tools\build-release.ps1` 里加 `Copy-ReleaseText`：把发布包中的这两份文件按字节归一成 UTF-8 无 BOM + LF，再打进 zip。打包结果不再随检出环境的 `core.autocrlf` 变化。

自己验证：把仓库另克隆一份，用 `git config core.autocrlf true` 检出，再跑一遍 `tools\build-release.ps1`，两个 zip 应当给出同一个哈希。

## 靠什么做到一致

配置集中在 `Directory.Build.props`：

- `Deterministic=true`：编译器不再写入随机的 MVID 与时间戳。
- `PathMap=$(MSBuildThisFileDirectory)=/_/,$(NuGetPackageRoot)=/_/nuget/`：源码路径与 NuGet 缓存路径都映射成固定前缀（两条映射用逗号分隔）。后一半是 v0.4.1 补的——只映射仓库根不够，全局包目录不同会让 `McDl.Core.dll` 的 MVID 变化。`$(NuGetPackageRoot)` 为空时这条自动跳过。
- `DeterministicSourcePaths=true` 与 `EmbedUntrackedSources=true`：源码路径的确定性由 SDK 与 SourceLink 负责。
- `ContinuousIntegrationBuild=true`：默认打开，在本地也生效，避免「本地和 CI 用了不同参数」。命令行传 `-p:ContinuousIntegrationBuild=false` 可覆盖。
- `DebugType=portable`：net48 上 SourceLink 要求 portable PDB。
- `PublishRepositoryUrl`、`RepositoryUrl`、`RepositoryType`、`IncludeSourceRevisionInInformationalVersion`：把仓库地址与提交号写进程序集属性。
- `RestorePackagesWithLockFile=true`：各项目旁生成 `packages.lock.json`，依赖版本钉死。

另外 `tools\build-release.ps1` 里的 `Copy-ReleaseText` 负责把进 zip 的 `README.md`、`LICENSE.txt` 归一成 UTF-8 无 BOM + LF，避免打包结果跟着检出环境的行尾设置变。

`BUILD-INFO.txt` 里记录的 `dotnet --version` 与 git commit 是这套东西的两个外部输入。提交号还写进了程序集：右键 `McDl.exe` → 属性 → 详细信息，「产品版本」结尾的 `+<commit>` 就是它，和 `BUILD-INFO.txt` 里的应该一致。这一步只能说明「这个包自称来自哪份提交」，证明力来自你按同一份源码和同一 SDK 重新构建并拿到同一个哈希。

## 怎么验

先看要复现的那次发布附带的 `BUILD-INFO.txt`，里面有三条完整命令（restore 与两次 build）。照抄，别自己换写法：

```powershell
git clone https://github.com/Ranecc/McDl.git build-a
cd build-a
git checkout v0.4.0

dotnet --version    # 应当是 10.0.301
dotnet restore mcdl.sln --locked-mode
dotnet build src\McDl.App\McDl.App.csproj -c Release -t:Rebuild --nologo -p:Version=0.4.0 -p:FileVersion=0.4.0.0 -p:AssemblyVersion=0.4.0.0
(Get-FileHash src\McDl.App\bin\Release\net48\McDl.exe -Algorithm SHA256).Hash
(Get-Item src\McDl.App\bin\Release\net48\McDl.exe).Length
```

换一个目录重来一遍，比较两个哈希。要对照已发布的包，就解压并从里面取 exe：

```powershell
Expand-Archive .\McDl-0.4.0-win.zip -DestinationPath .\released
(Get-FileHash .\released\McDl.exe -Algorithm SHA256).Hash
```

两个数一样，说明发布包里的 exe 可以由公开源码构建出来。也可以直接和 `McDl-0.4.0.exe.sha256` 比对。

## 为什么不承诺任意命令行

版本号是构建输入。`dotnet build mcdl.sln -c Release` 不带 `-p:Version`，`tools\build-release.ps1` 会传 `-p:Version=0.4.0 -p:FileVersion=0.4.0.0 -p:AssemblyVersion=0.4.0.0`，两者产出的 exe 不同——各自稳定，但互不相等。验证发布包时只认 `BUILD-INFO.txt` 里那几条命令。

同理，`packages.lock.json`、`global.json` 也都是输入。改了它们，哈希必然变。

## 复现的边界

**zip 里包含 `README.md` 与 `LICENSE.txt`。** 这两份文件一改，zip 的哈希就变，exe 的哈希不受影响。所以引用哈希时必须注明对应的提交或 tag，否则数字对不上是正常的。行尾同理：v0.4.1 之前它们跟着检出环境的 `core.autocrlf` 走，现在脚本会把它们归一成 LF 再打包，见上文「发布包里的文本文件，行尾也要归一」。

**PDB 没有发布。** SourceLink 的价值在别人拿着发布包调试时能按 PDB 找到源码；发布 zip 里不含 PDB，所以对最终用户的验证帮不上忙。

**依赖版本是输入，不是保证。** Newtonsoft.Json 13.0.4、Costura.Fody 6.2.0、Microsoft.SourceLink.GitHub 8.0.0 都锁在 `packages.lock.json` 里。锁文件变了，哈希必然变。

**SDK 补丁版会改变输出，已经踩过。** `global.json` 现在是 `rollForward: disable`，只认 10.0.301；换成 10.0.302、10.0.303 这类补丁版，`dotnet` 会直接拒绝构建，而不是给出一个哈希不同的包。别为了绕开这条去改 `global.json`——改了就回到 v0.4.0 那种两边对不上的状态。

**Fody / Costura 是黑盒。** 它会在编译后改写程序集。当前版本在 6.2.0 上实测是确定性的；升级 Costura 之后要重新比对哈希，不能沿用旧结论。

**没有 .git 的时候哈希会变。** `InformationalVersion` 的提交号来自 git 元数据。解压源码 tarball 构建，或者树上带未提交改动，这个字段会缺失或不同，自然对不上。

**CI 与本机的比对结果：v0.4.4 起一致。** 之前三次都先从「哈希不同」开始下钻：v0.4.0 是 SDK 补丁版不同，v0.4.1 是 NuGet 缓存路径没进 `PathMap` 且发布包文本文件行尾不同，v0.4.4 之前还剩下 PDB 绝对路径写进 PE 头这一层——都是解压后逐文件比、再比 MVID 才定位到的。这种下钻方法值得留着：大小相同、哈希不同，就别停在 zip 层。`ci.yml` 里那条「另开工作树再 build 一次并比对」的自检现在在 runner 上是跑通的（v0.4.4 起）。

**这不等于安全性。** 可复现构建解决的是「发布的 exe 是否来自这份源码」，解决不了「源码里有没有后门」。两个问题要分开看。
