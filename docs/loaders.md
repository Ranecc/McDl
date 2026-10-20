# 加载器

McDl 装三类加载器：Fabric、Forge、NeoForge。它们走两条完全不同的路——Fabric 只需要读一份 meta，Forge 系则要把官方 installer 下载下来、用本机 Java 执行。

入口是 `LoaderInstallers.For(kind)`，返回 `ILoaderInstaller`；`ApplyAsync` 在解析阶段被调用，可以往 `plan.Files` 加文件、往 `plan.PostDownload` 注册“等游戏文件到位后再做”的动作。加一种加载器的步骤见 [architecture.md](architecture.md#扩展点)。

## Fabric

Fabric 不需要本地 Java，也不跑任何程序。

1. `FabricLoader.FetchAsync` 请求 `https://meta.fabricmc.net/v2/versions/loader/<游戏版本>/<loader版本>`。包里没写 loader 版本时，先请求 `/v2/versions/loader/<游戏版本>`，取第一个 `stable: true` 的版本；没有稳定的就取第一条。
2. `FabricLoaderMeta.ClientLibraries()` 把 `launcherMeta.libraries.common` 和 `.client` 合并，按 Maven 坐标去重。server 列表不要。
3. 每个库按 `VanillaResolver.MavenRelativePath` 算路径，放进 `libraries/`；URL 直接用 meta 里给的那一条，`SourceKind` 是 `Other`，所以不走镜像。
4. `FabricLoader.Apply` 在 `versions\<profileId>\<profileId>.json` 写一份启动配置：`inheritsFrom` 指向原版版本，`mainClass` 用 meta 里的 `launcherMeta.mainClass.client`，`libraries` 原样放进去。启动器看到 `inheritsFrom` 就会把原版的库和 jar 一起算上。

这条路径的失败点基本只有网络。meta 挂了或者网络不通时，日志会显示对应 URL。

## Forge / NeoForge

两者共用 `ForgeInstaller`，靠构造函数里的 `LoaderKind` 区分。官方 installer 自带处理器和库下载逻辑，McDl 只负责把它请下来、跑起来、检查产物。

**解析阶段**（`ForgeInstaller.ApplyAsync`）：

1. 算出 installer 的 Maven 坐标。Forge 是 `net.minecraftforge:forge:<游戏版本>-<构建号>:installer`，基地址 `https://maven.minecraftforge.net/`；NeoForge 从 1.20.2 起是 `net.neoforged:neoforge:<独立版本号>:installer`，基地址 `https://maven.neoforged.net/releases/`，1.20.1 还是 `net.neoforged:forge:<游戏版本>-<构建号>:installer`。
2. 下载目标固定在 `<root>\.mcdl\installers\<文件名>`。这个目录不受版本切换影响，重装时如果文件还在，SHA1 校验会跳过下载。
3. 从原版元数据里读 `javaVersion.majorVersion`；没有就按游戏版本兜底（见下面的表）。
4. 往 `plan.PostDownload` 塞一个动作。

**执行阶段**（下载完所有文件之后）：

1. `JavaLocator.Find(requiredJava)` 找 Java；找不到就抛错，错误里写清需要的版本。
2. 起 `java.exe -jar <installer> --installClient <root>`，工作目录是 `<root>`，不弹窗口，stdout / stderr 都转发到日志，每行加 `[forge] ` 或 `[neoforge] ` 前缀。
3. 最多等 10 分钟。超时会杀进程并抛 `TimeoutException`；用户取消会杀进程并抛 `OperationCanceledException`。
4. 非零退出码会把最后 20 行输出带进异常。
5. 检查 `versions/` 下有没有新出现的、带 `<id>.json` 的目录，优先认 `forge-<版本>` / `neoforge-<版本>`。找不到就报“installer 正常退出，但没有出现新的版本目录”。
6. 如果这是整合包安装，再写一份 `versions\<包名>\<包名>.json`，`inheritsFrom` 指向刚装出来的加载器版本。

安装器自己会下载处理器和库，所以这一步比纯 Fabric 慢，也更依赖网络。日志里能看到 installer 的进度。

installer 发起的所有下载都是它自己直连 `launchermeta.mojang.com`、`libraries.minecraft.net` 等官方域名，McDl 既不知道也改不了。所以“installer 退出码 0 但什么都没装”几乎总是这些域名不可达（代理、hosts、加速工具），排查顺序：先在终端 `curl` 一下这些域名，再考虑 Java 与目录权限。`VersionId` 校验就是为了不让这种情况被当成成功吞掉。

### 版本拼接

包的清单可能写 `47.2.0`，也可能写完整的 `1.20.1-47.2.0`。`ComposeInstallerVersion` 的规则：

- 已经以 `<游戏版本>-` 开头的，原样使用，不重复拼。
- NeoForge 且游戏版本 ≥ 1.20.2，版本号就是独立号，不拼游戏版本。
- 其余情况拼成 `<游戏版本>-<构建号>`。

包没有声明加载器版本时直接报错，不会去猜最新版：

```
整合包没有写 Forge 的版本号，没法自己挑最新的 installer；可以在包的依赖里补上版本，或者先单独装原版再补加载器。
```

## Java 版本怎么定

优先信 Mojang 的版本元数据 `javaVersion.majorVersion`。元数据没写时按游戏版本兜底：

| Minecraft | Java |
| --- | --- |
| 1.20.5 及以后 | 21 |
| 1.18 – 1.20.4 | 17 |
| 1.17 | 16 |
| 1.16.5 及更早 | 8 |

`JavaLocator.Discover()` 的查找顺序：

1. `JAVA_HOME`（同时试 `<home>\bin\java.exe` 和 `<home>\java.exe`，有的安装脚本把 `JAVA_HOME` 指到 bin）。
2. `PATH` 里的每个目录。
3. 注册表：`HKLM\SOFTWARE\JavaSoft\Java Runtime Environment`、`Java Development Kit`，以及 `HKLM\SOFTWARE\Eclipse Adoptium`（安装器把目录写在 `Path`，旧版写 `JavaHome`）。
4. `Program Files` 和 `Program Files (x86)` 下的 `Java`、`Eclipse Adoptium`、`Microsoft` 子目录。
5. 额外根目录里的 Mojang runtime：递归找 `java.exe`，只认父目录叫 `bin` 的。CLI 目前没有传这个参数，`Discover` 的接口已经留着。

找到路径不代表能用，每个候选都会跑一次 `java -version`，从输出里解析主版本（`1.8.0_402` → 8，`21.0.1` → 21）。跑不起来的直接丢掉。

`Find(requiredMajor)` 先找主版本完全相等的，找不到再找“高于要求里最低的那个”。原因是老 Forge 的 installer 有时只认自己那一代 Java，拿 Java 21 去跑会失败。

## 找不到 Java 怎么办

报错原文：

```
Forge 的 installer 需要 Java 17 或更高版本，但本机没找到能用的 java.exe。可以装一个 JDK 17+，或者把 java.exe 所在目录加进 PATH，然后再装一次。
```

按这个顺序处理：

1. 装一个对应主版本的 JDK（Adoptium Temurin、Microsoft Build of OpenJDK、Oracle JDK 都行）。
2. 如果已经装了但没被发现，把 `java.exe` 所在目录加进 `PATH`，或者设 `JAVA_HOME`。
3. 确认 `java -version` 在同一个终端里能跑。
4. 重跑安装。已经下好的文件会跳过，只会重新执行 installer。

不需要为了下载原版或装 Fabric 去装 Java。

## Quilt

不支持。`LoaderSpec` 能识别 `quilt-loader` / `quilt`，但 `IsSupported` 返回 false，`ModpackSource` 在解析阶段就会拒绝，提示：

```
Quilt 加载器不在支持范围内，可以先单独安装原版，再用别的工具补加载器。
```

这是范围决定，不是遗漏。装完原版后可以用 Quilt 自己的安装器补上，McDl 不会去碰那一步。

## 镜像

Fabric 的 meta 和库 URL 都是 `SourceKind.Other`，直连，不走 BMCLAPI；Forge / NeoForge 的 installer 走 `Maven` 类别，会先官方、失败后切镜像。installer 自己发起的下载不受 McDl 的 `MirrorRouter` 控制，那部分网络问题只能在 installer 日志里看。
