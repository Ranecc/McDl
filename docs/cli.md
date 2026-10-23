# 命令行

命令行版的目标是让安装过程可脚本化、可复现。它和 GUI 用同一个 `Installer`，只是把进度画成一行。

## 编译

```powershell
dotnet build src\McDl.Cli\McDl.Cli.csproj -c Release
.\src\McDl.Cli\bin\Release\net48\mcdl.exe --help
```

发布 zip 里只有 GUI 的 exe，`mcdl.exe` 需要自己编译。也可以不生成 exe，直接用 `dotnet run`：

```powershell
dotnet run --project src\McDl.Cli\McDl.Cli.csproj -c Release -- list --limit 3
```

`--` 后面的参数原样传给 `mcdl`。下文命令里的 `mcdl` 都指 `src\McDl.Cli\bin\Release\net48\mcdl.exe`；想直接敲命令，先把这个目录加进 `PATH`，或者写全路径。

## 命令

```
mcdl list [--type release|snapshot] [--limit N]
mcdl plan <version|packfile> [--root DIR] [--no-assets]
mcdl install <version> [--root DIR] [--no-assets] [--threads N]
mcdl modpack <file> [--root DIR] [--no-assets] [--threads N]
mcdl verify <version|pack.zip> --root DIR [--no-assets] [--threads N]
```

不带参数、`-h`、`--help`、`help` 都打印帮助。未知命令打印 `unknown command: xxx` 并返回 1。

### 公共参数

| 参数 | 含义 | 默认 |
| --- | --- | --- |
| `--root DIR` | 目标 `.minecraft` 目录 | 当前目录下的 `.minecraft`；`verify` 下必填 |
| `--no-assets` | 不下载 assets，也不下资源索引 | 下载 |
| `--threads N` | 并发下载数 | 8 |
| `--type` | 只用于 `list`，按版本类型过滤 | 不过滤 |
| `--limit N` | 只用于 `list`，最多列多少行 | 40 |

参数可以写成 `--root D:\Games\.minecraft` 或 `--root=D:\Games\.minecraft`。解析器只认以 `--` 开头的选项，未知选项会被忽略而不是报错；`--root` 后面跟的路径不要以 `--` 开头。

`--root` 会做 `Path.GetFullPath`，相对路径按当前工作目录展开。目录不存在会创建。

## list

列出官方版本清单。每次都会重新拉 `version_manifest_v2.json`，不缓存。

```powershell
mcdl list --limit 3
```

```
versions: 920

26.4-snapshot-5    snapshot   2026-10-20T12:15:06+00:00
26.4-snapshot-4    snapshot   2026-10-13T12:05:46+00:00
26.4-snapshot-3    snapshot   2026-10-06T12:54:26+00:00
```

`versions:` 是清单里的总条数，不是过滤后的数量。第一列是版本 id，第二列是 `release` / `snapshot` / `old_beta` / `old_alpha`，第三列是发布时间（UTC，原样输出）。清单读不出来时返回 1。

## plan

只解析，不下载。会打印文件数、总体积、按类别拆分的明细，以及要解压的 natives。

参数可以写版本号，也可以写一个已存在的整合包文件；文件存在时按整合包解析，这样装之前能先看包要下多少东西。

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
  lwjgl-jemalloc-3.3.1-natives-windows.jar
  lwjgl-openal-3.3.1-natives-windows.jar
  lwjgl-opengl-3.3.1-natives-windows.jar
  lwjgl-stb-3.3.1-natives-windows.jar
  lwjgl-tinyfd-3.3.1-natives-windows.jar
  lwjgl-3.3.1-natives-windows.jar
```

`plan` 不是只读的：它会把 `versions\<id>\<id>.json` 和 `assets\indexes\<index>.json` 写进 `root`。拿正在用的游戏目录做实验前先想一下。

版本号支持精确 id，也支持 `latest` 和 `latest-snapshot`。找不到时输出 `version not found in manifest: xxx`，返回 1。

26.3 的规模是 5212 个文件、579.0 MB，其中 assets 5147 个文件、462.4 MB。不同版本差异主要在 assets。

## install

原版安装。先打印计划，然后一行原地刷新的进度条，最后是汇总。

```powershell
mcdl install 1.6.4 --no-assets --root .testmc\cli-doc --threads 8
```

```
resolving 1.6.4 ... 1.6.4 (1.6.4), 19 files, 11.6 MB

done: 19 downloaded, 0 already present, 0 failed
root: E:\DEV\WechatBotWorkspace\mcdl\.testmc\cli-doc
```

再跑一次同样的命令：

```
resolving 1.6.4 ... 1.6.4 (1.6.4), 19 files, 11.6 MB

done: 0 downloaded, 19 already present, 0 failed
root: E:\DEV\WechatBotWorkspace\mcdl\.testmc\cli-doc
```

进度条的格式是：

```
[#########--------------------]  31.4%  180.2 MB/573.1 MB  8.1 MB/s  assets/objects/...
```

日志行以 `· ` 开头，进度条用 `\r` 覆盖同一行。重定向到文件时进度条可能只剩最后一行，这是 `Console.WindowWidth` 和覆盖写共同作用的结果；脚本里只关心最后的 `done:` 行和退出码即可。

单文件失败不会立刻中断，所有文件跑完后汇总。有失败时返回 1，并把第一条失败写到 stderr。

## modpack

```powershell
mcdl modpack .\某个整合包.mrpack --root D:\Games\.minecraft --threads 8
```

接受四种格式，按内容探测，不看扩展名：

- Modrinth：`.mrpack`，索引 `modrinth.index.json`
- CurseForge / HMCL 导出：`manifest.json`
- MCBBS：`mcbbs.packmeta`
- HMCL：`modpack.json`，且 `manifest.type` 必须是 `hmcl`

流程和 `install` 一样，只是解析阶段会多出加载器。装 Fabric 时日志类似：

```
· 识别为 modrinth 整合包：Example Pack 1.0
· 准备安装 Fabric 0.15.11
· Fabric 0.15.11 已加入启动配置
```

装 Forge 时会在下载完成后运行 installer：

```
· 下载 Forge 1.20.1-47.2.0 的官方 installer：forge-1.20.1-47.2.0-installer.jar
· 用 Java 17.0.12 运行 Forge installer，目标目录 D:\Games\.minecraft
[forge] ...
· Forge 1.20.1-47.2.0 安装完成，版本目录 forge-1.20.1-47.2.0
```

不支持的加载器在解析阶段就失败，不会先下一半文件。格式细节见 [packs.md](packs.md)，Java 要求见 [loaders.md](loaders.md)。

## verify

只解析、不下载，然后逐个复核磁盘上已有的文件：在不在、大小、SHA1。

```powershell
mcdl verify 1.21.4 --root D:\Games\.minecraft
```

```
resolving 1.21.4 ... 1.21.4 (1.21.4), 69 files
root: D:\Games\.minecraft
verifying ...
校验 69 个文件：全部一致
```

目标可以是版本号，也可以是一个已存在的整合包文件，按整合包解析。有文件缺失或哈希不符时逐条列出，返回 2：

```
verifying ...
校验 69 个文件：缺失 1 个，哈希不符 1 个
  不符 1.21.4.jar：D:\Games\.minecraft\versions\1.21.4\1.21.4.jar（大小 28335587 / 28335587 字节）
  缺失 jackson-annotations-2.13.4.jar：...\jackson-annotations-2.13.4.jar
```

`--root` 在 `verify` 下是必填的：默认的 `./.minecraft` 常常是空目录，「全部缺失」的结论没有意义。缺 `--root` 或缺目标时打印 usage 并返回 1。

校验只报告，不删除、不自动修复。补齐用 `install`：干净的文件按 SHA1 跳过，只重新下载缺失与不符的那些。语义与边界见 [verification.md](verification.md)。

## 退出码

| 码 | 含义 |
| --- | --- |
| 0 | 成功（`list` / `plan` / `verify` 正常完成且全部一致，`install` / `modpack` 没有失败文件） |
| 1 | 参数错误（含 `verify` 缺 `--root`）、解析失败、下载失败、installer 失败 |
| 2 | `verify` 发现缺失或哈希不符；或被 Ctrl+C 取消 |

取消时 stderr 只有一行 `cancelled`。已经下好的文件保留，重跑会跳过。

2 有两种来源，脚本里要区分的话：先看 stderr 有没有 `cancelled`，或者在 `verify` 的上下文里把 2 解释成「文件有问题」。

## 脚本里的用法

```powershell
$root = 'D:\Games\.minecraft'
.\mcdl.exe plan 1.20.1 --root $root
if ($LASTEXITCODE -ne 0) { throw 'plan failed' }
.\mcdl.exe install 1.20.1 --root $root --threads 16
if ($LASTEXITCODE -ne 0) { throw 'install failed' }
.\mcdl.exe verify 1.20.1 --root $root
switch ($LASTEXITCODE) {
    0 { '文件一致' }
    2 { '有文件缺失或被改，重跑 install 补齐' }
    default { throw 'verify 执行失败' }
}
```

不需要先清目录。`install` 对已经存在且 SHA1 正确的文件会直接跳过，适合放在启动脚本或部署脚本里反复跑。
