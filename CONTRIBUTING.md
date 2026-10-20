# 参与贡献

感谢你愿意为 McDl 出一份力。本文档说明开发环境、代码规范与提交流程，请在动手前花几分钟读一遍。

## 开发环境

- **操作系统**：Windows（项目依赖 WinForms 与 .NET Framework 4.8，无法在 Linux/macOS 上完整构建）
- **编译环境**：.NET SDK 8 或更高版本。SDK 只用于编译，目标框架是 `net48`
- **运行环境**：.NET Framework 4.8。Windows 10/11 已内置，无需单独安装
- **可选**：Visual Studio 2022（17.8+）能直接打开各 `.csproj`，但不是必需，命令行构建即可

安装 SDK 后可以确认版本：

```powershell
dotnet --version
```

## 构建与运行

```powershell
# 图形界面（产物：src\McDl.App\bin\Release\net48\McDl.exe）
dotnet build src\McDl.App\McDl.App.csproj -c Release

# 命令行（产物：src\McDl.Cli\bin\Release\net48\mcdl.exe，程序集名为 mcdl）
dotnet build src\McDl.Cli\McDl.Cli.csproj -c Release
```

直接运行编译产物：

```powershell
.\src\McDl.App\bin\Release\net48\McDl.exe

.\src\McDl.Cli\bin\Release\net48\mcdl.exe list --limit 20
.\src\McDl.Cli\bin\Release\net48\mcdl.exe plan 1.20.1
.\src\McDl.Cli\bin\Release\net48\mcdl.exe install 1.20.1 --root "D:\Games\mc-1.20.1" --threads 16
```

发布包由 `tools\build-release.ps1` 生成，产物是 `dist\McDl-<版本>-win.zip`：

```powershell
pwsh -File tools\build-release.ps1 -Version 0.1.0
```

调试时建议先跑一次小版本（例如 `1.6.4`）、一次现代版本（例如 `1.21.4`），这两种路径分别覆盖 legacy 虚拟资源树与新式资源结构。

## 目录结构与分层规则

```
src/McDl.Core/    下载引擎与解析逻辑（类库）
src/McDl.App/     WinForms 图形界面
src/McDl.Cli/     命令行入口
tools/            发布、截图、UI 自动化脚本
```

分层规则是硬性约束，PR 会按此检查：

1. **`McDl.Core` 不包含 UI**：不得引用 WinForms、任何界面控件或弹窗；不得直接读写用户界面状态，进度与日志通过回调/事件向外抛出。
2. **`McDl.App` 与 `McDl.Cli` 只做表现层**：它们只依赖 `McDl.Core`，两者之间不互相引用。
3. **新功能优先放 Core**：能放进 `McDl.Core` 的逻辑就不要写在界面里，并保证命令行同样可用——这样 GUI 与 CLI 的行为才不会分叉。
4. 谨慎引入第三方依赖：考虑到 `net48` 与单文件发布的体积，非必要不加包；确实需要时请在 PR 描述里说明理由。

## 代码风格

- 缩进 4 个空格，不使用 Tab
- 局部变量优先用 `var`（类型一目了然时）
- 异步方法以 `Async` 结尾，例如 `DownloadAsync`
- 每个文件使用 UTF-8 编码，包含中文字符串的源文件不要加 BOM
- 注释中英混排都可以，但**以可读性优先**：解释「为什么这么做」，而不是复述代码
- 公开类型与方法的命名遵循 .NET 惯例（PascalCase），私有字段用 `_camelCase`

## 提交规范

采用接近 [Conventional Commits](https://www.conventionalcommits.org/) 的风格：

```
<type>(<scope>): <简要说明>
```

常用 type：

| type | 用途 |
| --- | --- |
| `feat` | 新功能 |
| `fix` | 修复缺陷 |
| `docs` | 文档变更 |
| `refactor` | 重构（不改变行为） |
| `test` | 测试相关 |
| `chore` | 杂项（依赖、脚本、构建） |
| `ci` | CI / 工作流配置 |

要求：

- 一次提交只做一件事，避免把重构与功能混在一个提交里
- 说明用中文或英文都可以，但同一 PR 内保持一致
- 涉及行为变化的提交，请同步更新 `CHANGELOG.md` 的 `[Unreleased]` 小节

## PR 流程

1. 先开 issue 讨论需求或缺陷，确认方向后再动手（很小的文档修复可跳过）
2. Fork 仓库，从 `main` 拉出分支，分支名建议 `feature/xxx`、`fix/xxx`、`docs/xxx`
3. 按 `.github/pull_request_template.md` 填写 PR 描述
4. 至少**本地验证过一次真实版本的完整安装**：下载、SHA1 校验、natives 解压、资源文件落地都要走通；只编译通过不算验证
5. 维护者 review 通过后合并，合并方式以维护者习惯为准

## 如何新增一个「下载来源」

项目当前支持官方与镜像两类下载地址（镜像通过地址改写实现）。如果要接入一个新的下载来源，一般思路是：

- 把实现放在 `src/McDl.Core` 内，与现有的地址改写、失败降权逻辑放在一起，不要写进 GUI 或 CLI
- 在 Core 里提供一个统一的入口，让上层（App / Cli）无需关心具体来源，只感知「选到了哪个地址」
- 保证同一次安装中任意文件都可以独立回退到可用来源，而不是全局切换
- 补充文档：如果仓库中存在 `ARCHITECTURE.md`，请在其中同步说明新来源的规则与优先级

具体的类型名与调用方式请以仓库现有代码为准（先读 `src/McDl.Core` 下的相关实现），本文档不绑定具体实现细节。

## 行为准则

- 讨论对事不对人，欢迎直接指出问题，但请给出可复现的依据
- 本项目不提供、不分发任何游戏内容，请勿提交与 EULA 冲突的改动或资源
