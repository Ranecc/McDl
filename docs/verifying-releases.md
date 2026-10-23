# 校验发布包

发布包有两种校验：哈希对得上、签名对得上。两件事都做完，再谈可复现构建。

## 一次发布有哪些文件

以 `v0.4.0` 为例：

| 文件 | 是什么 |
| --- | --- |
| `McDl-0.4.0-win.zip` | 单文件版，`McDl.exe` 里已嵌入 Core 与 Newtonsoft.Json，旁边只有 `README.md` 和 `LICENSE.txt` |
| `McDl-0.4.0-win-unbundled.zip` | 不打包依赖版，`McDl.exe` + `McDl.Core.dll` + `Newtonsoft.Json.dll` + `README.md` + `LICENSE.txt` |
| `McDl-0.4.0.exe` | 从单文件版里拿出来的那个 exe，内容与 zip 内的完全一样，方便只想要 exe 的人 |
| `SHA256SUMS` | 两个 zip 的 SHA-256，每行 `<hash>  <文件名>`（两个空格） |
| `SHA256SUMS.sig` | `SHA256SUMS` 的 cosign 签名（base64） |
| `SHA256SUMS.pem` | 签名时用的短期证书 |
| `McDl-0.4.0.exe.sha256` | 单独一行 `<hash>  McDl.exe`，给 `McDl-0.4.0.exe` 用 |
| `BUILD-INFO.txt` | 版本、git describe、完整 commit、工作树是否干净、`dotnet --version`、三条完整构建命令、内嵌依赖、两个 zip 与 exe 的 SHA256 |
| `RELEASE-NOTES.md` | 这个版本改了什么。它同时是 Release 页面的正文和这里的一个资产文件 |
| `sbom.json` | 软件物料清单，列出用到的依赖及版本 |

两个 zip 里都没有 PDB。`McDl-0.4.0.exe` 与 zip 里的 `McDl.exe` 是同一个文件，哈希相同。

`SHA256SUMS` 只覆盖两个 zip。exe 的哈希在 `McDl-<版本>.exe.sha256` 里，那一行只写 `McDl.exe`，不指向哪个包——解压单文件版，或者直接用 `McDl-<版本>.exe`，对着它算都可以。

## 第一步：对哈希

Windows 上把文件下到同一个目录，然后：

```powershell
Get-Content .\SHA256SUMS
Get-FileHash .\McDl-0.4.0-win.zip -Algorithm SHA256
Get-FileHash .\McDl-0.4.0-win-unbundled.zip -Algorithm SHA256
```

两个哈希分别和 `SHA256SUMS` 里对应行一致即可。文件多时用脚本：

```powershell
$bad = 0
Get-Content .\SHA256SUMS | ForEach-Object {
    if (-not $_.Trim()) { return }
    $expected, $name = $_ -split '\s+', 2
    $name = $name.Trim()
    $actual = (Get-FileHash ".\$name" -Algorithm SHA256).Hash
    if ($actual -eq $expected.ToUpperInvariant()) { "OK   $name" }
    else { $bad++; "FAIL $name（期望 $expected，实际 $actual）" }
}
if ($bad -gt 0) { throw "$bad 个文件校验失败" }
"全部通过"
```

Linux / macOS 上，文件名要对得上，所以先进到文件所在目录：

```bash
cd ~/Downloads/McDl-0.4.0
sha256sum -c SHA256SUMS
```

`SHA256SUMS` 由 Windows 上的 PowerShell 生成。如果行尾是 CRLF，`sha256sum -c` 会报格式错误，先去一遍回车：

```bash
tr -d '\r' < SHA256SUMS | sha256sum -c -
```

只拿到 exe 时：

```powershell
Get-Content .\McDl-0.4.0.exe.sha256
Get-FileHash .\McDl.exe -Algorithm SHA256
```

哈希通过只说明「下载到的字节和发布清单一致」，前提是发布清单本身可信。下一步解决这个前提。

## 第二步：验签名

签名对象是 `SHA256SUMS`，不是 zip。先装 [cosign](https://docs.sigstore.dev/cosign/system_config/installation/)，然后：

```bash
cosign verify-blob SHA256SUMS \
  --signature SHA256SUMS.sig \
  --certificate SHA256SUMS.pem \
  --certificate-identity-regexp "^https://github.com/Ranecc/McDl/.github/workflows/release.yml@refs/tags/v" \
  --certificate-oidc-issuer https://token.actions.githubusercontent.com
```

Windows 上用 `cosign.exe` 跑同一条命令，PowerShell 里把行尾的 `\` 换成反引号或写成一行。

通过时输出 `Verified OK`。三条约束缺一不可：

- `--certificate-oidc-issuer` 固定为 GitHub Actions 的 OIDC 签发方。
- `--certificate-identity-regexp` 要求签名来自本仓库的 `release.yml`，且触发 ref 是 `v` 开头的 tag。换一个 workflow 或分支签出来的都不算。
- 正则匹配的是完整身份串，`refs/tags/v` 之后的部分由实际 tag 决定，所以这条命令验的是「某个 v 开头的 tag」，不绑定具体版本号。要绑死版本，把正则改成 `...@refs/tags/v0.4.0`。

这是 keyless 签名：没有长期私钥，签名时 GitHub Actions 用 OIDC 换一张几分钟有效的证书，签名同时记入 Rekor 透明日志。验签要联网（取 Fulcio 根证书与 Rekor 条目），断网环境下会失败，这属于预期。证书本身过期不影响验证，时间点由透明日志固定。

`SHA256SUMS.sig` 与 `SHA256SUMS.pem` 必须和 `SHA256SUMS` 一起使用。只改其中任何一个，验签都会失败。

## 各证明什么

| 手段 | 能证明 | 不能证明 |
| --- | --- | --- |
| SHA-256 比对 | 下载的 zip 与 `SHA256SUMS` 记录的字节一致，传输中没有被截断或篡改 | `SHA256SUMS` 本身是真的 |
| cosign 验签 | `SHA256SUMS` 由本仓库 `release.yml` 在某次 `v*` tag 上签名，签名链可追溯 | zip 里的代码没有恶意行为 |
| `BUILD-INFO.txt` + 程序集「产品版本」 | 这个包自称来自哪次提交、用哪个 SDK、哪条命令构建 | 只是自述，可以伪造 |
| 自己重建并比对 exe 哈希 | 二进制确实能由那份公开源码在该 SDK 下构建出来 | 不覆盖签名，也不覆盖 zip 容器 |

推荐顺序：先验签（确认清单可信），再对哈希（确认文件没被换），想知道源码层面是否干净就自己重建一次，见 [reproducible-builds.md](reproducible-builds.md)。

这几条证的都是**发布包**：exe 与两个 zip。装完之后磁盘上的 `.minecraft` 是否还和官方清单一致，是另一回事，用 `mcdl verify` 复核，见 [verification.md](verification.md)。两者不可互相替代——发布包验签管不到运行之后的文件，装完的复核也证明不了 exe 本身干净。

## 需要保留意见的地方

- **exe 不在 `SHA256SUMS` 里，它的哈希也没有签名。** `SHA256SUMS` 只覆盖两个 zip；`McDl-0.4.0.exe` 与 `McDl-<版本>.exe.sha256` 都是方便文件，`SHA256SUMS.sig` 不覆盖它们。要验 exe，走 zip 那条链：验 `SHA256SUMS` → 对 zip 哈希 → 解压。单拿一个 exe 时，能做的只有比对 `BUILD-INFO.txt` 里的提交号与「产品版本」。
- **SBOM 不校验内容。** `sbom.json` 说明用了哪些依赖及版本，不保证二进制与清单一一对应，只能辅助排查。
- **可复现性验证的范围。** 实测同 SDK、同提交、两个不同目录 clean build 的 `McDl.exe` 与打包产物都逐字节一致，GitHub Actions 上的产物也一致（v0.4.4 起连续复核到 v0.6.0）。仍未覆盖：不同补丁版 SDK、不同 Windows 版本、非 Windows 环境。详见 [reproducible-builds.md](reproducible-builds.md)。另外 zip 里含 `README.md`，README 一改 zip 哈希就变、exe 不变，所以引用哈希要注明提交。细节见 [reproducible-builds.md](reproducible-builds.md)。
- **`SHA256SUMS` 不覆盖 `BUILD-INFO.txt`、`RELEASE-NOTES.md`、`sbom.json`。** 这几份文件被替换不会被发现。
- **签名者身份是仓库的 workflow，不是某个人。** 有仓库写权限的人能改 workflow 并发新 tag；签名能证明「来自这个仓库的发布流程」，不能证明「作者本人审过每一行」。
