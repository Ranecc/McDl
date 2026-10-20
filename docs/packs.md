# 整合包

McDl 只从整合包里拿两样东西：对应的 Minecraft 版本，和要装的加载器。包里的模组、配置、资源包、光影都不会下载，文件清单会被读出来，但只用于统计和展示。

四种格式都由 `Packs` 目录下的读取器处理，统一成 `PackManifest`：

| 格式 | 索引文件 | `Id` | 读取器 |
| --- | --- | --- | --- |
| Modrinth | `modrinth.index.json` | `modrinth` | `ModrinthPackFormat` |
| CurseForge / HMCL 导出 | `manifest.json` | `curseforge` | `CurseForgePackFormat` |
| MCBBS | `mcbbs.packmeta` | `mcbbs` | `McbbsPackFormat` |
| HMCL 自己的格式 | `modpack.json` | `hmcl` | `HmclPackFormat` |

入口是 `PackFormats.Read(path)`，按 `PackFormats.All` 的顺序逐个 `CanRead`，第一个认领的负责解析。`CanRead` 只打开 zip 看目录，不会解压整包；`Read` 只读索引那一个条目。

## 四种格式的字段差异

### Modrinth（`.mrpack`）

索引是 `modrinth.index.json`，关键字段：

```json
{
  "name": "...",
  "versionId": "...",
  "dependencies": { "minecraft": "1.20.1", "fabric-loader": "0.15.11" },
  "files": [
    { "path": "mods/foo.jar", "hashes": { "sha1": "..." }, "downloads": ["https://..."], "fileSize": 12345 }
  ]
}
```

- `dependencies` 是对象。McDl 按 `fabric-loader`、`forge`、`neoforge`、`quilt-loader` 的顺序找，只取第一个。
- `files[]` 带直链和 SHA1，是四种格式里信息最全的。但按上面的边界，这些字段不会进入下载队列。

### CurseForge / HMCL 导出（`manifest.json`）

这两种包结构一致，所以共用一个读取器。CurseForge 桌面端和 HMCL 的“导出为 CurseForge 格式”都会生成它：

```json
{
  "name": "...",
  "version": "1.0",
  "minecraft": {
    "version": "1.20.1",
    "modLoaders": [ { "id": "forge-47.2.0", "primary": true } ]
  },
  "files": [ { "projectID": 238222, "fileID": 4800000, "required": true } ]
}
```

- `minecraft` 在规范里是对象；读取器也接受直接写成字符串 `"minecraft": "1.20.1"` 的变体和顶层 `minecraftVersion`。
- `files[]` 只有 `projectID` 和 `fileID`，**没有文件名和直链**。这是 McDl 不下载它们的原因之一：真实文件名要查 CurseForge API，需要 API key，还要接受它的再分发条款。读取器给这些条目生成 `mods/project-<id>-file-<id>.jar` 这样的占位路径，一眼能看出出处，比编一个像真的文件名诚实。
- 一条 `files` 记录缺 `projectID` 或 `fileID` 会直接报错，因为无法判断它对应哪个文件。

### MCBBS（`mcbbs.packmeta`）

国内老包常见。字段和 CurseForge 接近，但同一个信息有好几种写法，读取器都兼容：

- `minecraft` 可以是对象，也可以是字符串；对象里还试 `version`、`versionId`、`minecraftVersion`、`gameVersion`。
- 找不到 `minecraft.version` 时看顶层 `minecraftVersion`。
- `modLoaders` 通常挂在 `minecraft` 下，也有导出工具写在顶层，两处都会试。
- `files[]` 的哈希可能是 `hash` 字符串，也可能是 `hashes` 对象；两种都读。
- `files[].path` 必填，缺了算清单不完整，直接报错。

### HMCL（`modpack.json`）

HMCL 自己的格式，索引里有 `manifest.type`：

```json
{
  "manifest": { "type": "hmcl", "name": "...", "version": "..." },
  "minecraft": { "version": "1.20.1", "modLoaders": [ { "id": "fabric-0.15.11", "primary": true } ] },
  "files": [ ... ]
}
```

`CanRead` 只做粗筛（`modpack.json` 文本里出现 `"hmcl"`），`Read` 再严格检查 `manifest.type == "hmcl"`。光有一个叫 `modpack.json` 的文件不算，否则会把别家的包误认成 HMCL。

## 加载器 id 怎么写

CurseForge、MCBBS、HMCL 的 `modLoaders[].id` 是“名字-版本”：

```
fabric-0.15.11
forge-47.2.0
neoforge-20.4.237
quilt-loader-0.27.1
```

`PackLoaderIds.Parse` 在第一个 `-` 处切开，前半截交给 `LoaderSpec.FromKey`：

| 写法 | 结果 |
| --- | --- |
| `fabric-loader` / `fabric` | Fabric |
| `forge` | Forge |
| `neoforge` | NeoForge |
| `quilt-loader` / `quilt` | Quilt |
| `minecraft` | 原版（`LoaderKind.None`） |
| 其他 | `Other`，不支持 |

`modLoaders` 里有多条时优先 `primary: true`，没有就取第一条；一条都没有就是纯原版包。Modrinth 的 `dependencies` 是另一套写法，由 `ModrinthPackFormat.PickLoader` 自己按固定顺序挑。

Forge 侧还有一层版本拼接：Forge 的 installer 版本是 `<游戏版本>-<构建号>`；NeoForge 从 1.20.2 起用独立版本号（如 `20.4.237`），1.20.1 仍沿用 Forge 的拼法。包里如果已经写了完整的 `1.20.1-47.2.0`，不会再重复拼前缀。细节在 [loaders.md](loaders.md)。

## 我们只装本体

装一个整合包，实际发生的是：

1. 读出 `PackManifest`，拿到 `MinecraftVersion` 和 `Loader`。
2. 用这个游戏版本跑一遍原版解析，下载客户端、库、natives、assets。
3. 如果有加载器，委托给 `ILoaderInstaller`；Fabric 加库和启动配置，Forge / NeoForge 额外跑 installer。
4. 在 `versions\<包名>-<版本>\` 下写一个 `inheritsFrom` 指向加载器版本的启动配置，让启动器里能按包名看到它。

包里的 `files[]` 全部跳过。这样做的好处是失败面小、不用处理模组之间的依赖和下载源；坏处是装完不能直接玩，得让启动器把模组补齐。

## 明确不支持的

**Quilt**。`LoaderSpec` 能识别 `quilt-loader` 和 `quilt`，但 `IsSupported` 返回 false，解析阶段就停下，提示“Quilt 加载器不在支持范围内，可以先单独安装原版，再用别的工具补加载器”。不是拼写问题，就是 Quilt。

未知加载器同理：走 `LoaderKind.Other`，报“暂时不支持 <名字> 加载器”。

其他限制：

- 只认 zip 容器。整个目录、7z、rar 都不行。
- 索引必须在 zip 根目录或子目录里；`PackArchive.FindEntry` 会先找精确路径，再找以 `/<文件名>` 结尾的条目。
- 索引里的游戏版本必须能在官方版本清单里找到，否则报“不在官方版本列表里”。
- 目前不读 `overrides/`，也不做路径穿越检查——因为压根不解压这些条目。
