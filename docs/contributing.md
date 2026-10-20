# 参与开发

## 环境

Windows 10/11，装 .NET SDK 8 或更高。仓库用 SDK 风格的项目文件，编译目标是 net48，运行时用系统自带的 .NET Framework 4.8。Visual Studio 2022 可选，不装也能用 `dotnet` 和任意编辑器。

```powershell
dotnet build mcdl.sln -c Release
pwsh -NoProfile -File tools\test.ps1
```

不要用 `dotnet test`，原因见 [testing.md](testing.md)。改完 Core 之后至少跑一次 `dotnet build src\McDl.Core\McDl.Core.csproj -c Release`，目前基线是 0 警告 0 错误，别把警告带进来。

## 代码风格

`.editorconfig` 已经写好了缩进和换行，编辑器支持就直接生效：

- 4 个空格，C# 左大括号另起一行；csproj / props 是 2 个空格。
- UTF-8 无 BOM，CRLF；源文件里的中文直接写，不要转义。`Directory.Build.props` 设了 `CodePage 65001`，就是为这个。
- `LangVersion` 是 latest，`Nullable` 关闭，没有 implicit usings，写 `using` 要老实列出来。
- 公开 API 只在必要的地方加 XML 注释；`CS1591` 已经关掉，不会因为缺注释报警告。公开类型和成员的清单看 [api-surface.md](api-surface.md)（由 `tools\api-surface.ps1` 生成，别手改）。

已有代码的风格可以概括成：少抽象，失败信息带上下文，能一句话说清的不写三句。

## 注释约定

这是这个仓库比较在意的一条。

- **写“为什么”，不写“是什么”。** `// 先清空目录，避免换架构后留着旧 dll` 有用；`// 清空目录` 没用。
- **一段实现细节只在一个地方讲。** 详细流程写进 `docs\`，代码里最多留一行 `详见 docs/xxx.md`。两边都写一遍，改的时候必漏一边。
- **中文为主。** 新代码用中文注释；老文件里的英文注释可以顺手改成中文，但不要为了翻译而翻译——能删的直接删。
- **公开类型的 XML 注释面向调用者。** 参数、返回值、异常里不显然的部分写清楚；`<summary>` 不要复述类型名。
- **用户能看到的文案**优先走 `Text.Get("key")`（`Support\Text.cs`）。存量硬编码还没迁完，新代码不要再加裸字符串。注意这只是一个接缝，不是已经支持多语言。

## 提交信息

用前缀加一句话，中文或英文都行，仓库里现存的提交以中文为主：

```
feat: 整合包支持 CurseForge manifest.json
fix: 续传时 Range 返回 200 会覆盖整个文件
docs: 补充 Forge installer 的 Java 要求
chore: 升级 Newtonsoft.Json
```

前缀用 `feat` / `fix` / `docs` / `refactor` / `test` / `chore` / `ci`。一次提交只做一件事；格式化、重命名和功能改动不要混在一起，review 时很难看。

不要提交 `bin/`、`obj/`、`dist/`、`.testmc/`、`.shots/`，这些都在 `.gitignore` 里。也不要提交 `.minecraft` 目录或整合包文件。

## 改哪一层

- 加下载源、改安装流程：从 `Api\IInstallSource` 和 `Api\Installer` 入手。
- 加整合包格式：实现 `Packs\IPackFormat`，登记到 `PackFormats.All`。先读 [packs.md](packs.md)。
- 加加载器：实现 `Loaders\ILoaderInstaller`，登记到 `LoaderInstallers.Instances`。先读 [loaders.md](loaders.md)。
- 改下载、镜像、校验：`Engine\DownloadEngine.cs`、`Engine\MirrorRouter.cs`。动这块要补测试，镜像排序和 URL 分类都有现成用例。
- 改界面：`src\McDl.App`，不要引用 Core 的内部类型，也不要绕过 `Installer`。
- 分层和扩展点的完整说明在 [architecture.md](architecture.md)。

## PR 流程

1. 先开 issue 说清楚要解决什么；小的文档、错别字 PR 可以跳过。
2. 从 `main` 拉分支，分支名不需要特定格式，能看出主题就行。
3. 改完跑 `dotnet build mcdl.sln -c Release` 和 `pwsh -NoProfile -File tools\test.ps1`。
4. 涉及真实下载的改动，按 [testing.md](testing.md) 手工装一次；在 PR 描述里写清楚测了哪个版本、什么网络环境、有没有 Java。
5. 提 PR，描述里写动机、改动点、验证方式。CI 会跑构建和测试，作业名是 `build`。
6. 等 review。涉及 `Loaders\`、`Packs\` 的改动最好拉上对应维护者看一眼。

## CI 会做什么

push 到 `main`、PR、手动触发都会跑 `windows-latest`：

- restore Core / App / Cli；
- Release 构建 App 和 Cli；
- `tools\test.ps1`；
- 上传 `McDl.exe` 作为 artifact，保留 14 天。

CI 不下载游戏文件，不验证 Forge installer，也不测 GUI。这些只能本地验。

## 不要做的事

- 不要复制 PCL 或其他启动器的代码、资源或文案。PCL 的 LICENCE 不允许二次分发，这个项目是 clean-room：只参考公开协议和公开 API。
- 不要在 Core 里引用 `System.Windows.Forms`。
- 不要把 `ResolveAsync` 写成只读的假设——它现在就会写版本 JSON 和资源索引，改前端时别指望它不碰磁盘。
- 不要在下载引擎里加 Minecraft 特判。引擎只认 `FileTask`。
