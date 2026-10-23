# 参与贡献

完整的开发环境、代码风格、注释约定、提交信息和 PR 流程都在 [docs/contributing.md](docs/contributing.md)。

快速版本：

```powershell
dotnet build mcdl.sln -c Release
pwsh -NoProfile -File tools\test.ps1
pwsh -NoProfile -File tools\check-leaks.ps1
```

不要用 `dotnet test`，原因见 [docs/testing.md](docs/testing.md)。分层和扩展点见 [docs/architecture.md](docs/architecture.md)。

泄漏检查是必跑项：文档、日志和示例里的本机路径一律写占位符（`<安装目录>`、`<用户名>`），不要贴真实的盘符、用户名或主机名。这些东西一旦提交就会留在 git 历史里，清理成本远高于写的时候多看一眼。CI 的 build 作业会跑同一个脚本，命中直接失败。
