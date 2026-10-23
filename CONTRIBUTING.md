# 参与贡献

完整的开发环境、代码风格、注释约定、提交信息和 PR 流程都在 [docs/contributing.md](docs/contributing.md)。

快速版本：

```powershell
dotnet build mcdl.sln -c Release
pwsh -NoProfile -File tools\test.ps1
```

不要用 `dotnet test`，原因见 [docs/testing.md](docs/testing.md)。分层和扩展点见 [docs/architecture.md](docs/architecture.md)。

文档、日志和示例里的本机路径一律写占位符（`<安装目录>`、`<用户名>`），不要贴真实的用户目录或网络共享路径。CI 的 build 作业会扫这两类内容，命中直接失败。
