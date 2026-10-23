# 可复现构建

结论先说：同一提交、同一个 SDK、两个不同目录，删掉 `bin` / `obj` 后用同一套命令构建，得到的 `McDl.exe` 逐字节一致。打包脚本产出的 exe 与两个 zip 也一致。GitHub Actions 与本机产物的一致性还没验证——CI 里加了自检，但没在 runner 上真跑过。

## 现状

| 场景 | 状态 |
| --- | --- |
| 同一 SDK、同一提交、同一目录，两次 clean build | 已实测：`McDl.exe` 的 SHA-256 相同 |
| 同一 SDK、同一提交，两个不同目录（各自删 `bin` / `obj`） | 已实测：`McDl.exe` 逐字节一致 |
| `tools\build-release.ps1` 在两个目录的产物（exe 与两个 zip） | 已实测：全部一致 |
| GitHub Actions 产物与本机产物 | 未验证。`ci.yml` 里有两次构建比对的自检，没在 runner 上跑过 |
| 不同 SDK 补丁版（10.0.301 与同 band 更高补丁） | 未验证，倾向于不一致，见下文 |
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

## 靠什么做到一致

配置集中在 `Directory.Build.props`：

- `Deterministic=true`：编译器不再写入随机的 MVID 与时间戳。
- `PathMap=$(MSBuildThisFileDirectory)=/_/`：PDB 里的源码路径统一映射成 `/_/`，换检出目录不影响结果。
- `DeterministicSourcePaths=true` 与 `EmbedUntrackedSources=true`：源码路径的确定性由 SDK 与 SourceLink 负责。
- `ContinuousIntegrationBuild=true`：默认打开，在本地也生效，避免「本地和 CI 用了不同参数」。命令行传 `-p:ContinuousIntegrationBuild=false` 可覆盖。
- `DebugType=portable`：net48 上 SourceLink 要求 portable PDB。
- `PublishRepositoryUrl`、`RepositoryUrl`、`RepositoryType`、`IncludeSourceRevisionInInformationalVersion`：把仓库地址与提交号写进程序集属性。
- `RestorePackagesWithLockFile=true`：各项目旁生成 `packages.lock.json`，依赖版本钉死。

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

**zip 里包含 `README.md` 与 `LICENSE.txt`。** 这两份文件一改，zip 的哈希就变，exe 的哈希不受影响。所以引用哈希时必须注明对应的提交或 tag，否则数字对不上是正常的。

**PDB 没有发布。** SourceLink 的价值在别人拿着发布包调试时能按 PDB 找到源码；发布 zip 里不含 PDB，所以对最终用户的验证帮不上忙。

**依赖版本是输入，不是保证。** Newtonsoft.Json 13.0.4、Costura.Fody 6.2.0、Microsoft.SourceLink.GitHub 8.0.0 都锁在 `packages.lock.json` 里。锁文件变了，哈希必然变。

**SDK 补丁版可能改变输出。** 同一个 feature band 的不同补丁版通常只修 bug，但编译器与 MSBuild 目标文件随 SDK 更新，没有逐版本验证过。`global.json` 只要求 `latestPatch`，所以装 10.0.3xx 里更高的补丁仍能构建，但不能假定哈希不变。

**Fody / Costura 是黑盒。** 它会在编译后改写程序集。当前版本在 6.2.0 上实测是确定性的；升级 Costura 之后要重新比对哈希，不能沿用旧结论。

**没有 .git 的时候哈希会变。** `InformationalVersion` 的提交号来自 git 元数据。解压源码 tarball 构建，或者树上带未提交改动，这个字段会缺失或不同，自然对不上。

**CI 这一环没闭合。** `ci.yml` 会在 runner 上把同一个提交切到另一个目录再 build 一次并比对 `McDl.exe`，但这条自检本身还没在 GitHub Actions 上跑过。跑过之前，「CI 产物与本机一致」只能算设计意图。

**这不等于安全性。** 可复现构建解决的是「发布的 exe 是否来自这份源码」，解决不了「源码里有没有后门」。两个问题要分开看。
