# 测试与验收

## 跑测试

```powershell
pwsh -NoProfile -File tools\test.ps1
```

脚本会编译 `tests\McDl.Tests`，再用 `xunit.console.exe` 跑 `bin\Release\net48\McDl.Tests.dll`，退出码和测试结果一致。默认 `-Configuration Release`，也可以传 `Debug`。

**不要用 `dotnet test`。** 新版 SDK 自带的 vstest ObjectModel 在 net48 测试宿主里加载不了 xUnit 2.x 适配器，会报 “no tests available”。`tools\test.ps1` 把 `xunit.runner.console` 2.9.3 从 NuGet 全局缓存里找出来直接跑，CI 用的也是这个脚本。

当前是 136 个用例（`[Theory]` 的每一行算一个），全绿大约 0.3 秒。

## 测试分三层

**纯函数。** 不碰磁盘和网络，直接调 internal 帮助函数。`InternalsVisibleTo` 让测试项目能看到 `VanillaResolver.ClassifierOf`、`ForgeInstaller.ComposeInstallerVersion` 这些内部成员。

- `RuleEvaluatorTests`：规则匹配。没有规则默认允许；OS 名字、架构、版本正则；allow 后接 disallow；features 门槛；坏正则被忽略。
- `VanillaResolverTests`：Maven 坐标转路径、classifier 解析、natives 分类器识别、架构别名（`natives-windows-arm64` 只匹配 arm64）、老版本 `natives` map 的 `${arch}` 替换、`TotalBytes` 把可选文件也算进去。
- `LoaderTests`：Java 版本行解析（`1.8.0_402` → 8、`21.0.1` → 21、非版本行返回 null）、Forge / NeoForge 的 installer 坐标和文件名、版本拼接（已带游戏版本前缀时不重复拼、NeoForge 1.20.2+ 用独立版本号）、缺版本时抛错、`RequiredJavaMajor` 的元数据优先与兜底、版本目录命名。
- `InstallVerifierTests`：一致即干净、哈希大小写不敏感、缺失、大小相同但内容不符、大小不符时不看哈希、计划没给哈希或大小、空计划与 null 计划、已取消的 token（空计划也要抛）、重复路径只查一次、进度不丢不重。

**合成 zip。** 测试自己生成临时 zip 和目录，不依赖仓库里的二进制夹具。

- `PackFormatTests`：为四种格式各造一个最小 zip，断言 `PackFormats.Read` 选的读取器、加载器、游戏版本；也覆盖异常路径——缺 `minecraft.version`、HMCL 的 `manifest.type` 不对、纯文本文件不是任何格式、CurseForge 缺 `projectID`/`fileID`。MCBBS 的宽松布局（`minecraft` 是字符串、加载器在顶层、`hash` vs `hashes`）单独有用例。
- `MirrorAndModpackTests`：URL 分类、官方 profile 排第一、失败 profile 沉到后面、未知 host 不改写；Modrinth 索引读取、缺索引/缺文件的拒绝；不支持的加载器报错前缀；`BuildProfileId` 把包名变成安全文件夹名；`ResolveRoot` 的默认值。
- `NativesUnpackerTests`：dll 被平铺解出、`META-INF/` 和 `*.sha1` 被跳过、重复解压前清掉旧文件、取消会被响应、null/空输入不炸。

**没有自动化端到端。** 需要真实网络和几百 MB 磁盘的安装不在单测里跑。下面这些只能手工验。

## 手工验收一次完整安装

先确认基线：

```powershell
dotnet build mcdl.sln -c Release
pwsh -NoProfile -File tools\test.ps1
```

然后走一遍原版。用 `.testmc` 这类目录，别拿正在玩的存档做实验：

```powershell
$mcdl = '.\src\McDl.Cli\bin\Release\net48\mcdl.exe'
$root = Join-Path (Get-Location) '.testmc\acceptance'
& $mcdl plan 1.20.1 --root $root
& $mcdl install 1.20.1 --root $root --threads 8
```

`plan` 的预期输出：3649 个文件、699.1 MB，其中 assets 3598 个文件。`install` 结束后检查：

```powershell
Test-Path "$root\versions\1.20.1\1.20.1.json"      # True
Test-Path "$root\versions\1.20.1\1.20.1.jar"       # True
Test-Path "$root\assets\indexes\5.json"            # True
(Get-ChildItem "$root\assets\objects" -Recurse -File).Count   # 3598
(Get-ChildItem "$root\versions\1.20.1\natives" -File).Count    # >0
```

再跑一次同样的 `install`，应该输出 `0 downloaded, 3649 already present, 0 failed`。这是断点续传和 SHA1 跳过逻辑最直接的验收。

装完之后校验一遍，应该干净、退出码 0。下面这一轮单独用 1.21.4、不带 assets：

```powershell
$vroot = 'D:\tmp\verify-e2e'
& $mcdl install 1.21.4 --root $vroot --no-assets
& $mcdl verify 1.21.4 --root $vroot    # 校验 69 个文件：全部一致
$LASTEXITCODE                          # 0
```

再故意弄坏一点东西——改掉 `versions\1.21.4\1.21.4.jar` 的一个字节、删掉一个 `libraries` 下的 jar——确认失败路径与修复路径：

```powershell
& $mcdl verify 1.21.4 --root $vroot
$LASTEXITCODE                          # 2

& $mcdl install 1.21.4 --root $vroot --no-assets    # done: 2 downloaded, 67 already present, 0 failed
& $mcdl verify 1.21.4 --root $vroot                 # 校验 69 个文件：全部一致
$LASTEXITCODE                                       # 0
```

改字节而不改大小的情形也要看一次：`verify` 里大小相同仍会算哈希，改坏的文件必须报「不符」，只改大小才是敷衍了事。语义见 [verification.md](verification.md)。

装一个 1.7 以前的版本，验证虚拟资源目录。例如：

```powershell
$legacyRoot = Join-Path (Get-Location) '.testmc\acceptance-legacy'
& $mcdl install 1.6.4 --root $legacyRoot
Test-Path "$legacyRoot\assets\virtual\legacy\..."   # 按索引里的名字展开
```

1.6.4 不含 assets 索引时不会生成 `virtual`；想验这一条，去掉 `--no-assets` 并选一个带 `virtual: true` 的旧版本，或者直接检查 `plan` 输出的 `asset index` 一栏。

加载器单独验：

```powershell
# Fabric：不需要 Java，日志里应出现“Fabric x.y.z 已加入启动配置”
& $mcdl modpack .\fabric-pack.mrpack --root $root

# Forge / NeoForge：需要本机 Java 17，日志里应出现 installer 输出和“安装完成”
& $mcdl modpack .\forge-pack.mrpack --root $root
Test-Path "$root\versions\forge-1.20.1-47.2.0\forge-1.20.1-47.2.0.json"   # True
```

Forge 的端到端需要在有 Java 的机器上跑，CI 不做。验收时至少确认 `versions` 下出现了加载器目录、`<包名>.json` 的 `inheritsFrom` 指向它、游戏能由启动器识别。

GUI 手工检查：

1. 双击 `McDl.exe`，版本列表能加载，搜索 `1.21` 有结果。
2. 选一个版本，改安装目录、线程数，开始安装，进度和日志在动。
3. 安装中关掉再打开，重跑同一版本，已存在文件被跳过。
4. 拖一个 `.mrpack` 进窗口，面板显示格式、MC 版本、加载器。
5. 拖一个 Quilt 包，提示不支持而不是开始下载。
6. 选一个 `.zip` 的 CurseForge 包，能识别 `manifest.json`。
7. 安装结束后应自动跑一次校验：干净时日志写「校验通过」，文件状态栏写「全部文件与官方哈希一致」。
8. 删掉一个已安装文件，点「校验文件」：应弹「文件校验未通过」的警告框、日志列出缺失项，且文件不会被删除或改写。按钮在解析出计划之前是灰的。

## 网络与失败路径

这些情况不容易复现，改下载逻辑时最好手动模拟：

- 官方源超时：抓包工具或 hosts 把 `piston-meta.mojang.com` 指到黑洞，确认自动切 BMCLAPI。
- 镜像返回 404/HTML：确认 SHA1 校验失败后重试、最终计入 `FailedFiles`。
- 下载中取消：Ctrl+C，进程应在几秒内退出，`.part` 文件保留。
- 目标目录只读：报错应带路径，GUI 会提示换目录。
- Forge installer 超时：只能改常量模拟，别在正常验收里等十分钟。

## CI 做什么

`.github\workflows\ci.yml`，push 到 `main`、PR、手动触发都会跑，同一分支的新提交会取消上一次运行。作业名是 `build`（ASCII，方便分支保护里搜），跑在 `windows-latest`：

1. `actions/checkout@v4`（`fetch-depth: 0`，可复现性自检要读历史）。
2. `actions/setup-dotnet@v4` 读 `global.json`。SDK 版本以这个文件为准，workflow 里不写死。
3. 打印 `dotnet --version`，留一行日志便于比对。
4. 检查编译输入的行尾：`*.cs`、`*.csproj`、`*.props`、`*.json`、`*.xml`、`*.targets` 里出现字节 13 就失败。
5. 检查本机路径：用户目录与 UNC 路径在文档、脚本里出现就失败（`D:\Games` 这类示例路径不拦）。
6. `dotnet restore mcdl.sln --locked-mode`。锁文件对不上就让 CI 失败，别让它悄悄解析新版本。
7. `dotnet build mcdl.sln -c Release`。
8. `pwsh -NoProfile -File tools\test.ps1`。
9. 可复现性自检：把同一个提交取到另一个工作树里再裸 build 一次，比对两处 `McDl.exe` 的 SHA256，不同就失败。
10. 从 `McDl.App.csproj` 读出版本号，交给 `pwsh -File tools\build-release.ps1 -Version <版本>`，产出与发布同一套文件。
11. 用 `dist\McDl-<版本>.exe.sha256` 校验自己刚打出来的 exe，对不上就失败。
12. 上传 artifact `mcdl-exe`：`dist\McDl-<版本>.exe`、`dist\McDl-<版本>.exe.sha256`、`dist\SHA256SUMS`，并打印各组件 SHA256。

第 10 步用的是与 Release 完全相同的命令和参数，所以打出来的 exe 与 Release 资产逐字节相同。具体哈希由构建时嵌进程序集的 commit 决定，每提交一次都会变，所以永远看产物旁边的 `McDl-<版本>.exe.sha256`，不要照抄文档里的数字。exe 大小通常在 385 KB 左右，这个数也不是校验依据。

分支保护要求的必需状态检查就是 `build` 这一个。CI 不跑真实下载，也不验 Forge installer。涉及网络和 Java 的改动需要本地按上面的步骤走一遍。
