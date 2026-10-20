# 参与贡献

完整的开发环境、代码风格、注释约定、提交信息和 PR 流程都在 [docs/contributing.md](docs/contributing.md)。

快速版本：

```powershell
dotnet build mcdl.sln -c Release
pwsh -NoProfile -File tools\test.ps1
```

不要用 `dotnet test`，原因见 [docs/testing.md](docs/testing.md)。分层和扩展点见 [docs/architecture.md](docs/architecture.md)。
