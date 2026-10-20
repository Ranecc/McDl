# McDl

McDl 是个 Minecraft 下载器。它把原版游戏或整合包需要的文件放进 `.minecraft`，然后交给启动器。

做它的起因很具体：2026 年 10 月 20 日，PCL2 从 2.99.0.0 开始拿掉了 MC 下载，同一天国内几个第三方启动器也陆续下线了下载功能。那之后想补一份干净的游戏文件，得自己去找版本清单、库文件、资源索引和一堆镜像地址。McDl 把这些收进一个 exe 里。

## 能下什么，不能下什么

能下的：

- 原版任意版本。客户端 jar、libraries、natives、资源索引和 assets 都会下，快照和远古版本也认。
- 整合包的游戏本体和加载器。Modrinth 的 `.mrpack`、CurseForge 或 HMCL 导出的 `manifest.json`、MCBBS 的 `mcbbs.packmeta`、HMCL 自己的 `modpack.json` 都能读，纯原版包也能装。
- Fabric、Forge、NeoForge。Fabric 从官方 meta 拿库列表；Forge 和 NeoForge 下载官方 installer，用你机器上的 Java 跑 `--installClient`。

不能做的：

- 不启动游戏。账号登录、正版验证、版本隔离都是启动器的事。
- 不装整合包里的模组、配置、资源包和光影。清单里的文件记录会读出来，但只用来识别版本，不下载。
- 不支持 Quilt。检测到会直接说清楚，不会硬装。也不支持 Linux 和 macOS，这个版本和 WinForms、.NET Framework 4.8 绑在一起。

## 怎么用

**图形界面**：Windows 10/11 上双击 `McDl.exe` 就行，不用先装 .NET。exe 大约 365 KB，核心库和 JSON 库都嵌在里面了。

左边是版本列表，可以搜索。选一个版本，确认安装目录（第一次打开时是 `%APPDATA%\.minecraft`），决定要不要一起下 assets，然后点「开始安装」。整合包可以直接把文件拖进窗口，或者点「整合包…」；窗口会显示识别出的格式、Minecraft 版本和加载器，日志里能看到更细的步骤。

assets 的勾选框会记住上次的选择。第一次运行时它是不勾的：整份资源通常占总下载量的九成左右，不勾就只下客户端、库和 natives。不勾也能装出本体，但游戏里会缺声音和语言文件。

**命令行**：命令行版在 `src\McDl.Cli`，编译出来是 `mcdl.exe`。它没有放进发布 zip，需要自己 build：

```powershell
dotnet build src\McDl.Cli\McDl.Cli.csproj -c Release
.\src\McDl.Cli\bin\Release\net48\mcdl.exe list --limit 3
```

下面命令里的 `mcdl` 都指这个 exe；想直接敲 `mcdl`，先把它的目录加进 `PATH`。

```
versions: 920

26.4-snapshot-5    snapshot   2026-10-20T12:15:06+00:00
26.4-snapshot-4    snapshot   2026-10-13T12:05:46+00:00
26.4-snapshot-3    snapshot   2026-10-06T12:54:26+00:00
```

先看一眼要下多少东西，再决定装不装：

```powershell
mcdl plan 1.20.1
```

```
name         : 1.20.1
version      : 1.20.1
asset index  : 5
root         : E:\DEV\WechatBotWorkspace\mcdl\.minecraft
files        : 3649
size         : 699.1 MB

  Assets     3598 files    621.5 MB
  Maven        50 files     55.6 MB
  Meta          1 files     22.0 MB

natives to unpack:
  lwjgl-glfw-3.3.1-natives-windows.jar
  ...
```

`plan` 只解析和打印，不下载；不过它会把版本 JSON 和资源索引写到目标目录，所以别拿一个正在玩的 `.minecraft` 做实验。

真正开始装：

```powershell
mcdl install 1.20.1 --root D:\Games\.minecraft --threads 8
mcdl install latest-snapshot --no-assets
mcdl modpack .\某个整合包.mrpack --root D:\Games\.minecraft
```

`install` 会先打印计划，然后是一条会原地刷新的进度条，结束时汇总「下载了多少、本来就有多少、失败多少」。中途 Ctrl+C 会取消；再跑一次，已经完整的文件按 SHA1 跳过，没下完的从 `.part` 接着下。

完整的命令和参数见 [docs/cli.md](docs/cli.md)。

## 文件放在哪

以 `install --root D:\Games\.minecraft` 为例：

```
D:\Games\.minecraft\
  versions\1.20.1\1.20.1.json            版本配置
  versions\1.20.1\1.20.1.jar             客户端 jar
  libraries\...                          库文件，按 Maven 目录排
  assets\indexes\5.json                  资源索引
  assets\objects\ab\abcdef...            资源本体，按 SHA1 分目录
  assets\virtual\5\...                   1.7 以前的虚拟资源目录
  resources\...                          更早版本的扁平资源目录
  .mcdl\installers\...                   Forge / NeoForge 的官方 installer
  versions\<整合包名>\<整合包名>.json     整合包装完后的启动配置，继承加载器版本
```

游戏目录里不会多出别的东西。`.mcdl` 是 McDl 自己的工作目录，删掉不影响已经装好的版本，下次装 Forge 时会重新下载 installer。

## 环境

Windows 10 或 11。.NET Framework 4.8 是系统自带的，Win10 1903 以后默认就有；不需要单独装运行时。

Java 只有 Forge 和 NeoForge 用得上。原版下载和 Fabric 安装都不碰 Java。版本要求优先看 Mojang 的版本元数据；元数据没写时按下面兜底：

| Minecraft | 需要的 Java |
| --- | --- |
| 1.20.5 及以后 | 21 |
| 1.18 – 1.20.4 | 17 |
| 1.17 | 16 |
| 1.16.5 及更早 | 8 |

McDl 会在这些地方找 `java.exe`：`JAVA_HOME`、`PATH`、注册表里的 JavaSoft / Adoptium 项、`Program Files` 下的 `Java`、`Eclipse Adoptium`、`Microsoft` 子目录，以及官方启动器下发的 `runtime`。找到之后会实际跑一次 `java -version` 确认版本，不是只看路径。优先用主版本号正好匹配的那个，没有再退到「比要求高、但最低」的版本——老 Forge 的 installer 有时只认自己那一代 Java。

一个都找不到时，报错会写清楚要哪一代：

```
Forge 的 installer 需要 Java 17 或更高版本，但本机没找到能用的 java.exe。可以装一个 JDK 17+，或者把 java.exe 所在目录加进 PATH，然后再装一次。
```

## 镜像与代理

每个下载请求都带类别：版本元数据（Meta）、Maven 库、资源文件（Assets）、其他直链（Other）。Meta、Maven、Assets 会先走官方，失败后切 BMCLAPI；哪条线路失败一次，后面的请求里它的优先级就往后排，成功一次再恢复。整合包里的直链和 Fabric 的 meta 属于 Other，不走镜像。

程序没有单独的代理设置，HTTP 请求走 Windows 的系统代理。如果你平时靠代理上网，系统代理开着就行。

## 常见问题

**国内下载很慢，或者连不上官方源。**
先等它自己切 BMCLAPI，日志里能看到线路变化。两条都慢通常是网络本身的问题；如果开了代理，检查系统代理有没有生效。Fabric 的 meta 是直连，不受镜像影响。

**下到一半关掉了，要重新下吗？**
不用。再点一次「开始安装」，完整的文件会跳过，不完整的从 `.part` 续传。

**提示某个文件校验失败。**
重跑一次。如果还是同一个文件失败，把它删掉再跑；命令行会打印第一个失败的文件和原因。SHA1 对不上通常意味着中间有代理或镜像返回了错误页面。

**Forge 装到一半退出，或者 installer 超时。**
installer 最多跑 10 分钟，超时会被强制结束。先看日志里 installer 的最后 20 行；常见原因是 Java 版本不对、安装目录没写权限，或者网络卡在下载处理器。把 Java 加到 `PATH` 后重跑即可，已经下好的文件不会重下。

**Forge 的 installer 自己退出了，日志里只有几行，`versions` 下什么也没多。**
installer 是 Forge 官方写的程序，它内部直连 `launchermeta.mojang.com`、`libraries.minecraft.net` 这些官方域名，不走 McDl 的镜像。如果这些域名在你的网络里被代理、hosts 或者某些加速工具拦了，installer 会安安静静地失败（退出码还是 0）。先把这些域名放通，或者开系统代理，再重跑一次。McDl 会检查 `versions` 下有没有真的多出目录，没多出来就按失败报，不会假装成功。

**装完能直接启动吗？**
不能，McDl 不启动游戏。用 HMCL、PCL2 或官方启动器打开这个 `.minecraft`，Forge / NeoForge / Fabric 的版本配置已经写好了。整合包里的模组需要启动器另外安装，McDl 只负责本体和加载器。

**为什么 exe 只有三百多 KB？**
目标是 net48，运行时用系统自带的 .NET Framework；Core 和 Newtonsoft.Json 通过 Costura 嵌进 exe。换成自带运行时的 .NET 8 单文件，体积会到几十 MB。

**装到 `Program Files` 下面失败。**
那是权限问题，换一个普通目录，或者用管理员身份运行。

**日志在哪？**
图形界面的日志面板只保留当次运行的内容，没有写文件。需要留证据时用命令行，输出可以直接重定向。

## 构建与测试

```powershell
dotnet build mcdl.sln -c Release
pwsh -NoProfile -File tools\test.ps1
pwsh -NoProfile -File tools\build-release.ps1 -Version 0.2.1
```

`tools\test.ps1` 用 xunit.console 直接跑 net48 测试程序集；不要用 `dotnet test`，原因写在 [docs/testing.md](docs/testing.md)。发布脚本会把 exe、README 和 LICENSE 打进 `dist\McDl-<版本>-win.zip`，并打印 SHA256。

想改代码，先看 [docs/contributing.md](docs/contributing.md)；想弄清内部怎么分层，看 [docs/architecture.md](docs/architecture.md)。

## License

MIT，见 [LICENSE](LICENSE)。

代码是 clean-room 写的：行为参考 Mojang 的公开协议、Modrinth / CurseForge / MCBBS / HMCL 的公开格式，以及各个加载器自己的官方 API。没有复制 PCL 的代码。PCL 用的是自定义 LICENCE，不允许二次分发，这个项目也不想沾上那个麻烦。
