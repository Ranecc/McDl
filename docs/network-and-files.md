# 网络与本地文件

结论：McDl 只连下面列出的域名，只写安装目录和 `%APPDATA%\McDl\settings.json`。没有遥测、没有自动更新检查、不访问 GitHub API、运行时不下载任何 dll。Forge / NeoForge 安装过程中，官方 installer 会以本机 Java 进程自己联网，那一部分不走 McDl 的下载器。

本文件对应 0.4.0。域名清单是按源码里的 `MirrorRouter`、`VanillaResolver`、`FabricLoader`、`ForgeInstaller` 整理的。

## 会访问的域名

| 域名 | 用途 | 类别 | 走镜像 |
| --- | --- | --- | --- |
| `piston-meta.mojang.com` | 版本清单 `mc/game/version_manifest_v2.json`、各版本 JSON、资源索引 | Meta | 是 |
| `piston-data.mojang.com` | 版本元数据里给出的客户端 jar 等下载地址 | Meta | 是 |
| `launchermeta.mojang.com` | 早期版本的清单、版本 JSON、jar 与资源索引 | Meta | 是 |
| `libraries.minecraft.net` | 库文件（`libraries/`） | Maven | 是 |
| `resources.download.minecraft.net` | 资源对象 `assets/objects/<sha1 前两位>/<sha1>` | Assets | 是 |
| `assets.mojang.com` | 1.6 及更早版本的 legacy 资源 | Assets | 是 |
| `meta.fabricmc.net` | Fabric loader 版本列表与库清单 | Other | 否，直连 |
| `maven.fabricmc.net` | Fabric 库（URL 由 meta 给出）与库的 `.sha1` | Other | 否，直连 |
| `maven.minecraftforge.net` | Forge installer 及其 `.sha1` | Maven | 是 |
| `maven.neoforged.net` | NeoForge installer 及其 `.sha1` | Maven | 是 |
| `bmclapi2.bangbang93.com` | 镜像。根路径对应 Meta，`/maven` 对应 Maven，`/assets` 对应 Assets | 镜像 | — |

几点补充：

- 版本 JSON、资源索引里出现的下载地址可能落在上面几个域名的任意一个，分类按 URL 前缀判定，不按用途猜。
- `maven.quiltmc.org` 出现在镜像路由的 Maven 类别表里，但 McDl 不支持 Quilt，实际不会请求它。
- 整合包里的模组直链（Modrinth CDN、CurseForge 等）只被读进清单用于识别版本，不下载，因此不会产生对这些域名的请求。
- 程序不发心跳、不上报使用情况。除了下载请求本身，没有别的出站连接。
- 所有请求都带固定的 User-Agent：`mcdl/0.1 (+https://github.com/)`。这个字符串里的地址是占位，不是仓库地址，也不随版本变化。

## 镜像怎么选

Meta、Maven、Assets 三类各有两条线路：官方优先，失败后切 BMCLAPI。某条线路在某个类别里失败一次，该类别后续请求里它的优先级就下降；成功一次后恢复。类别之间互不影响——镜像站没有某个库时，不至于把版本清单也一起拖慢。

Other 类别不重写 URL，直接连。Fabric 的 meta 与库都在这一类。

不走镜像意味着：BMCLAPI 故障不影响 Fabric 安装，反过来 Fabric 的相关域名也享受不到镜像加速。

## 连接细节

- TLS 1.2 及以上，跟随重定向，允许 gzip / deflate。
- 单次请求超时 90 秒。每个 URL 最多尝试 3 次，退避为 300 ms × 2^(n-1)；镜像候选在 3 次都失败后依次切换。
- 下载中断后从 `.part` 续传：请求带 `Range`，服务器不认 206 就从头重下。
- 不设独立代理配置，走 Windows 系统代理。

## 会在本机写什么

安装目录（GUI 默认 `%APPDATA%\.minecraft`，命令行 `--root` 默认当前目录下的 `.minecraft`，两者都可改）：

```
<root>\
  versions\<版本>\<版本>.json          版本配置
  versions\<版本>\<版本>.jar           客户端 jar
  versions\<版本>\natives\             解压出来的 natives
  versions\<整合包名>\<整合包名>.json   整合包装配后的 profile
  libraries\...                        库，按 Maven 目录排布
  assets\indexes\<索引名>.json         资源索引
  assets\objects\<前两位>\<sha1>       资源对象
  assets\virtual\<索引名>\...          1.7 以前的虚拟资源树
  resources\...                        1.6 及更早的扁平资源
  .mcdl\installers\<文件名>            Forge / NeoForge 官方 installer
```

下载中的临时文件是 `<目标路径>.part`，校验通过后才改名成正式文件。中断后 `.part` 留着，下次接着下；校验不通过会重试，仍失败则保留现场并在结束时汇总报错。

安装目录之外只写一个文件：`%APPDATA%\McDl\settings.json`，存图形界面的安装目录、线程数和 assets 勾选状态。只有 GUI 读写它，命令行不碰。文件损坏时按默认值启动，不会因此打不开。写失败就静默放弃，不改用别的位置。

除此之外：

- 不写注册表。查找 Java 时只读 `HKLM\SOFTWARE\JavaSoft\...` 与 `HKLM\SOFTWARE\Eclipse Adoptium` 下的路径，不做任何写入。
- 不写日志文件。GUI 的日志只在窗口里，关掉即消失；命令行的输出走标准输出与标准错误，需要留档就重定向。
- 不写系统目录，不创建服务、计划任务或开机启动项。

## 会在本机执行什么

**原版与 Fabric：什么都不执行。** 原版只下载文件。Fabric 只下载 meta 里列出的库、为每个库取一次 `.sha1`、写版本 JSON，不运行任何程序。

**Forge 与 NeoForge：执行官方 installer。** 流程是：

1. 从 `maven.minecraftforge.net`（NeoForge 为 `maven.neoforged.net`）下载 installer，存到 `<root>\.mcdl\installers\`。
2. 下载前先取同一个 URL 加 `.sha1` 的上游校验文件，拿到 40 位哈希就交给下载器强校验：已存在且哈希匹配的文件跳过，新下载的文件在 `.part` 改名之前校验，不匹配就换镜像重试。
3. 拿不到 `.sha1`（404、超时、内容不像哈希）时不中止，日志写「拿不到 xxx 的上游 .sha1，这次不校验它，只校验安装结果」，然后照常安装。
4. 用本机 Java 执行 `java.exe -jar "<installer>" --installClient "<root>"`，工作目录是 `<root>`，窗口隐藏，输出转发到 McDl 日志并加 `[forge]` / `[neoforge]` 前缀。超时上限 10 分钟。
5. installer 正常退出后，检查 `versions\` 下是否真的出现了新的加载器目录。没出现就按失败上报，不信任退出码。

installer 自己会联网：它直连 `launchermeta.mojang.com`、`libraries.minecraft.net`、`maven.minecraftforge.net` 等域名拉处理器和库，这部分不经过 McDl 的镜像体系，McDl 也看不到它的请求，只能转述它的输出。这些域名被 hosts、代理或加速工具干扰时，installer 可能静默失败且退出码为 0——所以第 5 步才要检查目录。

installer 以当前用户身份运行，有当前用户的文件权限。McDl 不是沙箱。

## 完整性校验到什么程度

| 内容 | 校验方式 |
| --- | --- |
| 客户端 jar、libraries、natives、资源对象 | Mojang 元数据里的 SHA-1，逐文件校验 |
| Forge / NeoForge installer | Maven 上的 `.sha1`；拿不到时跳过并写日志 |
| Fabric 库 | 逐个取 `<库 URL>.sha1`；拿到就强校验，拿不到则这个库不校验 |

SHA-1 是上游给的，不是 McDl 自己算的。它能发现下载内容与上游声明不符（镜像返错、代理插页面、文件截断），不能证明上游本身可信。

Fabric 库的校验要看上游：McDl 会逐个去 maven 取 `<库 URL>.sha1`，取到就强校验，取不到（404、超时、内容不是哈希）就不校验那一个库，继续安装。结束时日志写「Fabric 库 N 个，其中 M 个能取到上游 .sha1 并校验」，M 明显小于 N 时就该怀疑镜像或网络。短板是「上游没给就等于没校验」：这种情况下如果链路上有人替换了库文件，McDl 发现不了。装完可以用启动器或手工比对，但 McDl 自己做不到。

## 明确不做的事

- 不检查更新，不连 GitHub API，不拉取版本公告。
- 不上报崩溃、使用统计或设备信息。
- 不下载模组、资源包、光影、配置文件——这些属于整合包内容，不在范围内。
- 不下载或替换 Java。需要 Java 时只在本机查找，找不到就报错并说明要装哪个版本。
- 不修改 hosts、不装证书、不改系统代理设置。
