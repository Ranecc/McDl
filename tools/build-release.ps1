#requires -Version 7.0
<#
.SYNOPSIS
    构建 McDl 的可发布产物：单 exe 的 zip、不内嵌的 zip、校验和、构建信息、SBOM。

.DESCRIPTION
    产出（默认在 dist\ 下）：
      McDl-<v>-win.zip            单文件 exe（Costura 内嵌 McDl.Core.dll 与 Newtonsoft.Json.dll）
      McDl-<v>-win-unbundled.zip  McDl.exe + McDl.Core.dll + Newtonsoft.Json.dll + README.md + LICENSE.txt
      McDl-<v>.exe                单文件 exe（方便只用 exe 的人直接比对哈希）
      McDl-<v>.exe.sha256         单行 "<hash>  McDl.exe"
      SHA256SUMS                  两个 zip 的 sha256（"<hash>  <文件名>\"，覆盖它自己以外）
      BUILD-INFO.txt              版本 / 提交 / SDK / 构建命令 / Costura 内嵌依赖
      RELEASE-NOTES.md            GitHub Release 正文
      sbom.json                   CycloneDX 1.5（从 packages.lock.json 生成）

    可复现构建：同一提交 + 同一 SDK + 同一版本号，在不同目录构建出的 McDl.exe 与 zip
    应逐字节相同。zip 用固定时间戳写入，因此 SHA256SUMS 也稳定。
    若 McDl.exe 正在运行，构建会因文件占用失败，脚本会先结束该进程。

.PARAMETER Version
    发布版本号，不带 v 前缀，例如 0.4.0。会作为构建属性注入 exe 的 AssemblyVersion/FileVersion。

.PARAMETER Configuration
    默认 Release。

.PARAMETER SkipBuild
    跳过编译，直接用 src\McDl.App\bin\<Configuration>\net48 下已有产物。

.PARAMETER OutputDirectory
    默认 <仓库根>\dist。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+([.-][0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string]$Configuration = 'Release',

    [switch]$SkipBuild,

    [string]$OutputDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot 'src\McDl.App\McDl.App.csproj'
$appOutDir = Join-Path $repoRoot "src\McDl.App\bin\$Configuration\net48"
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'dist' }
$dist = [IO.Path]::GetFullPath($OutputDirectory)
$tf = "net48"

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Checked([string]$Exe, [string[]]$Arguments) {
    Write-Host "    $Exe $($Arguments -join ' ')" -ForegroundColor DarkGray
    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "命令失败（exit $LASTEXITCODE）：$Exe $($Arguments -join ' ')"
    }
}

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

# 发布包里的文本文件统一成 UTF-8 无 BOM + LF。
# 检出时的换行由 .gitattributes 决定（Windows 上是 CRLF），直接 Copy-Item 会让同一提交
# 在本地与 CI 上打出不同的 zip；这里按字节归一，zip 内容就不随检出环境变化。
function Copy-ReleaseText([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source)) { throw "缺少发布用文件：$Source" }
    $text = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($Source))
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) { $text = $text.Substring(1) }
    $text = $text.Replace("`r`n", "`n")
    [IO.File]::WriteAllText($Destination, $text, (New-Object Text.UTF8Encoding($false)))
}

# 固定时间戳写入的 zip：同一批文件在不同机器/不同时间打包，字节一致。
function New-DeterministicZip {
    param(
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][object[]]$Entries  # @{ Path = 绝对路径; Name = zip 内文件名 }
    )

    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue

    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
    $fixed = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($entry in ($Entries | Sort-Object Name)) {
                $zipEntry = $zip.CreateEntry($entry.Name, [IO.Compression.CompressionLevel]::Optimal)
                # Zip 的 DOS 时间最小值为 1980-01-01，避免写入构建日期。
                $zipEntry.LastWriteTime = $fixed
                $entryStream = $zipEntry.Open()
                try {
                    $bytes = [IO.File]::ReadAllBytes($entry.Path)
                    $entryStream.Write($bytes, 0, $bytes.Length)
                }
                finally { $entryStream.Dispose() }
            }
        }
        finally { $zip.Dispose() }
    }
    finally { $stream.Dispose() }
}

function Get-CosturaBundledAssemblies {
    # Costura 把被内嵌的程序集以 <hash>.costura.<名字>.compressed 放在 obj\...\Costura 下。
    # 同一个程序集可能出现多次（引用程序集 vs 实际程序集），按名字去重。
    $costuraDir = Join-Path $repoRoot "src\McDl.App\obj\$Configuration\$tf\Costura"
    if (-not (Test-Path -LiteralPath $costuraDir)) { return @() }
    Get-ChildItem -LiteralPath $costuraDir -Filter '*.costura.*.compressed' |
        ForEach-Object {
            $_.Name -replace '^[0-9A-Fa-f]+\.costura\.', '' -replace '\.compressed$', ''
        } |
        Sort-Object -Unique |
        ForEach-Object { [pscustomobject]@{ Name = $_ } }
}

function Get-LockedPackages {
    # 从各项目的 packages.lock.json 汇总依赖，用于 SBOM。
    $lockFiles = Get-ChildItem -LiteralPath $repoRoot -Recurse -Filter 'packages.lock.json' -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj|dist)[\\/]' }
    $packages = @{}
    foreach ($lockFile in $lockFiles) {
        $lock = Get-Content -LiteralPath $lockFile.FullName -Raw | ConvertFrom-Json
        foreach ($framework in $lock.dependencies.PSObject.Properties) {
            foreach ($dep in $framework.Value.PSObject.Properties) {
                $name = $dep.Name
                $info = $dep.Value
                if ($info.type -ne 'Direct' -and $info.type -ne 'Transitive') { continue }
                $version = [string]$info.resolved
                if ([string]::IsNullOrWhiteSpace($version)) { continue }
                $key = "$name/$version"
                if (-not $packages.ContainsKey($key)) {
                    $packages[$key] = [pscustomobject]@{
                        Name    = $name
                        Version = $version
                        Hash    = [string]$info.contentHash
                    }
                }
            }
        }
    }
    $packages.Values | Sort-Object Name, Version
}

function ConvertFrom-NuGetHash([string]$ContentHash) {
    # NuGet 的 contentHash 是裸 base64；兼容带 "sha512-" 前缀的写法。返回大写十六进制。
    if ([string]::IsNullOrWhiteSpace($ContentHash)) { return $null }
    $value = $ContentHash.Split('-', 2)[-1]
    try {
        $bytes = [Convert]::FromBase64String($value)
    }
    catch {
        return $null
    }
    return (($bytes | ForEach-Object { $_.ToString('X2') }) -join '')
}

function New-StableGuid([string]$Seed) {
    # 由内容派生的稳定 UUID（CycloneDX serialNumber 需要 urn:uuid）。
    $hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Seed))
    $bytes = $hash[0..15]
    $bytes[6] = [byte](($bytes[6] -band 0x0F) -bor 0x50)  # version 5
    $bytes[8] = [byte](($bytes[8] -band 0x3F) -bor 0x80)  # RFC 4122 variant
    $hex = ($bytes | ForEach-Object { $_.ToString('X2') }) -join ''
    return '{0}-{1}-{2}-{3}-{4}' -f $hex.Substring(0, 8), $hex.Substring(8, 4), $hex.Substring(12, 4), $hex.Substring(16, 4), $hex.Substring(20, 12)
}

# ---------------------------------------------------------------- 前置检查
if (Get-Process -Name McDl -ErrorAction SilentlyContinue) {
    Write-Host '检测到 McDl.exe 正在运行，先结束进程以免 MSB3021 文件占用。' -ForegroundColor Yellow
    Stop-Process -Name McDl -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

$dotnetVersion = (& dotnet --version).Trim()
$commit = (& git -C $repoRoot rev-parse HEAD).Trim()
$describe = (& git -C $repoRoot describe --tags --always --dirty).Trim()
$dirty = $describe.EndsWith('-dirty')

Write-Step "构建 McDl $Version（$Configuration，SDK $dotnetVersion，提交 $($commit.Substring(0,8))$(if ($dirty) { ' + 未提交改动' })）"

$buildProps = @(
    "-p:Version=$Version",
    "-p:FileVersion=$Version.0",
    "-p:AssemblyVersion=$Version.0"
)

# ---------------------------------------------------------------- 1. 单文件 exe
if (-not $SkipBuild) {
    if (Test-Path -LiteralPath $appOutDir) { Remove-Item -LiteralPath $appOutDir -Recurse -Force }
    Invoke-Checked 'dotnet' (@('build', $appProject, '-c', $Configuration, '-t:Rebuild', '--nologo') + $buildProps)
}

$bundledExe = Join-Path $appOutDir 'McDl.exe'
if (-not (Test-Path -LiteralPath $bundledExe)) {
    throw "找不到构建产物：$bundledExe"
}

# 确认这确实是 Costura 内嵌过的 exe，避免把不内嵌的版本当成单文件发布。
$exeText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($bundledExe))
if (-not $exeText.Contains('costura.') ) {
    throw "构建产物看起来没有经过 Costura 内嵌（未找到 costura.* 资源）：$bundledExe"
}
$costuraAssemblies = @(Get-CosturaBundledAssemblies)

$stagingBundled = Join-Path $dist '.stage-bundled'
$stagingUnbundled = Join-Path $dist '.stage-unbundled'
foreach ($dir in @($stagingBundled, $stagingUnbundled)) {
    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir | Out-Null
}

Copy-Item -LiteralPath $bundledExe -Destination (Join-Path $stagingBundled 'McDl.exe')
foreach ($extra in @('README.md', 'LICENSE')) {
    $dest = if ($extra -eq 'LICENSE') { 'LICENSE.txt' } else { $extra }
    Copy-ReleaseText (Join-Path $repoRoot $extra) (Join-Path $stagingBundled $dest)
}
Copy-Item -LiteralPath $bundledExe -Destination (Join-Path $dist "McDl-$Version.exe")

# ---------------------------------------------------------------- 2. 不内嵌的 exe + 依赖
if (-not $SkipBuild) {
    if (Test-Path -LiteralPath $appOutDir) { Remove-Item -LiteralPath $appOutDir -Recurse -Force }
    # Rebuild 确保 Fody 不参与，得到未织入的 McDl.exe，同时 Core/Newtonsoft 留在输出目录。
    Invoke-Checked 'dotnet' (@('build', $appProject, '-c', $Configuration, '-t:Rebuild', '-p:DisableFody=true', '--nologo') + $buildProps)
}

$unbundledExe = Join-Path $appOutDir 'McDl.exe'
$unbundledText = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($unbundledExe))
if ($unbundledText.Contains('costura.')) {
    throw "DisableFody=true 的构建产物仍然包含 Costura 资源，无法作为不内嵌版本：$unbundledExe"
}

$unbundledFiles = @(
    @{ Source = $unbundledExe; Name = 'McDl.exe' },
    @{ Source = (Join-Path $appOutDir 'McDl.Core.dll'); Name = 'McDl.Core.dll' },
    @{ Source = (Join-Path $appOutDir 'Newtonsoft.Json.dll'); Name = 'Newtonsoft.Json.dll' },
    @{ Source = (Join-Path $repoRoot 'README.md'); Name = 'README.md' },
    @{ Source = (Join-Path $repoRoot 'LICENSE'); Name = 'LICENSE.txt' }
)
foreach ($file in $unbundledFiles) {
    if ($file.Name -eq 'README.md' -or $file.Name -eq 'LICENSE.txt') {
        Copy-ReleaseText $file.Source (Join-Path $stagingUnbundled $file.Name)
    }
    else {
        if (-not (Test-Path -LiteralPath $file.Source)) { throw "缺少不内嵌版本所需的文件：$($file.Source)" }
        Copy-Item -LiteralPath $file.Source -Destination (Join-Path $stagingUnbundled $file.Name)
    }
}
# Costura.dll 只是内嵌版本的装载器，不随不内嵌包分发。
if (Test-Path -LiteralPath (Join-Path $appOutDir 'Costura.dll')) {
    Write-Host '    已知会出现在输出目录的 Costura.dll 已按要求排除。' -ForegroundColor DarkGray
}

# ---------------------------------------------------------------- 3. 打包
Write-Step '打包 zip（固定时间戳，保证可复现）'

$zipBundled = Join-Path $dist "McDl-$Version-win.zip"
$zipUnbundled = Join-Path $dist "McDl-$Version-win-unbundled.zip"
New-DeterministicZip -Destination $zipBundled -Entries (Get-ChildItem -LiteralPath $stagingBundled -File | ForEach-Object { @{ Path = $_.FullName; Name = $_.Name } })
New-DeterministicZip -Destination $zipUnbundled -Entries (Get-ChildItem -LiteralPath $stagingUnbundled -File | ForEach-Object { @{ Path = $_.FullName; Name = $_.Name } })

foreach ($dir in @($stagingBundled, $stagingUnbundled)) { Remove-Item -LiteralPath $dir -Recurse -Force }

$artifacts = @($zipBundled, $zipUnbundled)
$hashes = @{}
foreach ($artifact in $artifacts) { $hashes[(Split-Path -Leaf $artifact)] = Get-Sha256 $artifact }

# SHA256SUMS：只覆盖两个 zip，不包含它自己。
$sumsPath = Join-Path $dist 'SHA256SUMS'
$sumLines = foreach ($artifact in $artifacts) {
    '{0}  {1}' -f $hashes[(Split-Path -Leaf $artifact)], (Split-Path -Leaf $artifact)
}
Set-Content -LiteralPath $sumsPath -Value $sumLines -Encoding ascii

# 只拿得到 exe 的人也需要能直接比对。
$exePath = Join-Path $dist "McDl-$Version.exe"
$exeHash = Get-Sha256 $exePath
Set-Content -LiteralPath (Join-Path $dist "McDl-$Version.exe.sha256") -Value ("{0}  McDl.exe" -f $exeHash) -Encoding ascii

# ---------------------------------------------------------------- 4. BUILD-INFO.txt
Write-Step '生成 BUILD-INFO.txt / RELEASE-NOTES.md / sbom.json'

$buildCommandBundled = 'dotnet build src\McDl.App\McDl.App.csproj -c {0} -t:Rebuild --nologo -p:Version={1} -p:FileVersion={1}.0 -p:AssemblyVersion={1}.0' -f $Configuration, $Version
$buildCommandUnbundled = 'dotnet build src\McDl.App\McDl.App.csproj -c {0} -t:Rebuild --nologo -p:DisableFody=true -p:Version={1} -p:FileVersion={1}.0 -p:AssemblyVersion={1}.0' -f $Configuration, $Version
$costuraDeps = @($costuraAssemblies | Where-Object { $_.Name -ne 'costura.dll' })
$hasCosturaLoader = @($costuraAssemblies | Where-Object { $_.Name -eq 'costura.dll' }).Count -gt 0
$costuraList = if ($costuraDeps.Count -gt 0) {
    ($costuraDeps | ForEach-Object { '  - {0}' -f $_.Name }) -join "`n"
} else {
    '  - McDl.Core.dll' + "`n" + '  - Newtonsoft.Json.dll'
}
if ($hasCosturaLoader) { $costuraList += "`n  （另内嵌 Costura.dll 装载器本身，它是 Costura 的运行时代码，不是项目依赖）" }

$buildInfo = @"
McDl $Version 发布构建信息
==========================

版本            : $Version
git describe    : $describe
git commit      : $commit
工作树           : $(if ($dirty) { '包含未提交改动（非正式发布构建）' } else { '干净' })
dotnet SDK      : $dotnetVersion
配置            : $Configuration
操作系统        : $([Environment]::OSVersion.VersionString)

构建命令（可复现构建请照抄）：
  dotnet restore mcdl.sln --locked-mode
  $buildCommandBundled
  $buildCommandUnbundled

Costura 内嵌的依赖：
$costuraList

产物（SHA256）：
  McDl-$Version-win.zip            $($hashes["McDl-$Version-win.zip"])
  McDl-$Version-win-unbundled.zip  $($hashes["McDl-$Version-win-unbundled.zip"])
  McDl-$Version.exe                $exeHash

不内嵌版本的文件清单：
  McDl.exe, McDl.Core.dll, Newtonsoft.Json.dll, README.md, LICENSE.txt
  （不含 Costura.dll——它只是单文件版本的装载器）

验证方式见 docs/verifying-releases.md。
"@
Set-Content -LiteralPath (Join-Path $dist 'BUILD-INFO.txt') -Value $buildInfo -Encoding utf8

# ---------------------------------------------------------------- 5. RELEASE-NOTES.md
$previousTag = (& git -C $repoRoot tag --sort=-creatordate) | Where-Object { $_ -ne "v$Version" } | Select-Object -First 1
if ($previousTag) {
    $compareUrl = "https://github.com/Ranecc/McDl/compare/$previousTag...v$Version"
} else {
    $compareUrl = "https://github.com/Ranecc/McDl/commits/v$Version"
}

$shortCommit = $commit.Substring(0, 12)
$bt = [char]96
$exeSumName = "McDl-$Version.exe.sha256"
$verifyScript = 'tools/verify-release.ps1'
$costuraListMd = ($costuraList -split "`r?`n" | ForEach-Object { $_ -replace '^\s*-\s*', '- ' }) -join "`n"
$releaseNotes = @"
## McDl $Version

- 提交：$shortCommit（$describe）
- .NET SDK：$dotnetVersion
- 配置：$Configuration

### 构建命令（完整，可照抄复现）

    dotnet restore mcdl.sln --locked-mode
    $buildCommandBundled
    $buildCommandUnbundled

### 下载

| 文件 | 说明 | SHA256 |
| --- | --- | --- |
| McDl-$Version-win.zip | 单文件 exe，依赖已内嵌 | $($hashes["McDl-$Version-win.zip"]) |
| McDl-$Version-win-unbundled.zip | exe + 依赖 dll | $($hashes["McDl-$Version-win-unbundled.zip"]) |

单文件 exe 的哈希：$exeHash（见 $bt$exeSumName$bt）。

### Costura 内嵌的依赖

$costuraListMd

### 验证

- 校验下载文件：$bt$verifyScript$bt；签名与哈希的验证方法见 [docs/verifying-releases.md](https://github.com/Ranecc/McDl/blob/main/docs/verifying-releases.md)。
- 该版本为可复现构建：同一提交 + 同一 SDK 重新构建可得到逐字节相同的 exe。

**完整变更**：$compareUrl
"@
Set-Content -LiteralPath (Join-Path $dist 'RELEASE-NOTES.md') -Value $releaseNotes -Encoding utf8

# ---------------------------------------------------------------- 6. sbom.json
$packages = @(Get-LockedPackages)
$components = foreach ($package in $packages) {
    $component = [ordered]@{
        type    = 'library'
        name    = $package.Name
        version = $package.Version
        purl    = "pkg:nuget/$($package.Name)@$($package.Version)"
    }
    $hex = ConvertFrom-NuGetHash $package.Hash
    if ($hex) { $component.hashes = @(@{ alg = 'SHA-512'; content = $hex }) }
    $component
}
$sbom = [ordered]@{
    bomFormat   = 'CycloneDX'
    specVersion = '1.5'
    serialNumber = 'urn:uuid:' + (New-StableGuid "McDl/$Version/$commit")
    version     = 1
    metadata    = [ordered]@{
        component = [ordered]@{
            type    = 'application'
            name    = 'McDl'
            version = $Version
            purl    = "pkg:github/Ranecc/McDl@v$Version"
        }
        tools     = @(@{ vendor = 'McDl'; name = 'build-release.ps1'; version = '1' })
    }
    components  = @($components)
}
$sbomJson = $sbom | ConvertTo-Json -Depth 10
Set-Content -LiteralPath (Join-Path $dist 'sbom.json') -Value $sbomJson -Encoding utf8

# ---------------------------------------------------------------- 汇总
Write-Step '完成'
Write-Host ("  {0,-34} {1,10:N0} bytes  {2}" -f (Split-Path -Leaf $zipBundled), (Get-Item $zipBundled).Length, $hashes["McDl-$Version-win.zip"])
Write-Host ("  {0,-34} {1,10:N0} bytes  {2}" -f (Split-Path -Leaf $zipUnbundled), (Get-Item $zipUnbundled).Length, $hashes["McDl-$Version-win-unbundled.zip"])
Write-Host ("  {0,-34} {1,10:N0} bytes  {2}" -f (Split-Path -Leaf $exePath), (Get-Item $exePath).Length, $exeHash)
Write-Host ("  {0,-34} {1}" -f 'sbom.json 组件数', $components.Count)
Write-Host ("输出目录：{0}" -f $dist)


