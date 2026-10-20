# 这个 PR 做了什么

<!-- 用几句话说明改动内容与动机。 -->

## 关联 issue

<!-- 例如：Fixes #12 / Closes #34；没有关联 issue 可以写「无」 -->

## 测试方式

<!--
说明你是怎么验证的，例如：
- 手动运行图形界面，安装了 1.21.4 / 1.6.4，均成功完成并校验通过
- 用 CLI 执行了 `mcdl.exe plan 1.20.1`，输出条目数与预期一致
- 取消后再继续，确认 .part 断点续传正常
-->

- 测试过的版本（McDl 版本 / 游戏版本）：
- 验证过的入口（图形界面 / 命令行）：

## 分层规则检查

> 本项目分为 `McDl.Core`（下载引擎与解析逻辑）、`McDl.App`（WinForms 图形界面）、`McDl.Cli`（命令行）三层，请确认：

- [ ] `McDl.App` 与 `McDl.Cli` 只依赖 `McDl.Core`，没有反向依赖
- [ ] `McDl.Core` 没有引用 WinForms 或任何 UI 类型
- [ ] 新功能优先放在 `McDl.Core`，并且 `McDl.Cli` 也能使用（如果适用）
- [ ] 没有引入不必要的第三方依赖（考虑 net48 与单文件发布体积）

## 提交前自检

- [ ] 已本地构建通过（`dotnet build src/McDl.App/McDl.App.csproj -c Release` 与 `dotnet build src/McDl.Cli/McDl.Cli.csproj -c Release`）
- [ ] 已更新 `CHANGELOG.md` 中 `[Unreleased]` 小节
- [ ] 代码风格与现有代码一致（4 空格缩进、文件 UTF-8、注释清晰）
- [ ] 没有提交 `bin/`、`obj/`、`dist/` 等构建产物
