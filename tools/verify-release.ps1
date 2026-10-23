#requires -Version 7.0
<#
.SYNOPSIS
    校验一次 McDl 发布下载是否完整、是否来自官方构建（含可选的 cosign 签名验证）。

.DESCRIPTION
    1) 按 SHA256SUMS 校验每个文件的 sha256；
    2) 若 dist\*.exe.sha256 存在，同时校验单文件 exe 的哈希；
    3) 本机装了 cosign 且存在 SHA256SUMS.sig / SHA256SUMS.pem 时，用 keyless 证书验证签名
       （证书身份必须指向本仓库 release.yml 的 tag 构建）。

    全部通过时退出码 0；任一哈希不符或签名验证失败时退出码 1，并逐条打印失败原因。

.PARAMETER Directory
    下载目录，默认 <仓库根>\dist。

.PARAMETER SumsFile
    校验和文件名，默认 SHA256SUMS。

.PARAMETER SkipSignature
    跳过 cosign 签名验证，只校验哈希。

.PARAMETER RequireSignature
    要求必须完成签名验证：缺少 cosign 或缺少 .sig/.pem 时视为失败（CI 里可用）。

.EXAMPLE
    pwsh -File tools\verify-release.ps1
    pwsh -File tools\verify-release.ps1 -Directory .\downloads -RequireSignature
#>
[CmdletBinding()]
param(
    [string]$Directory,
    [string]$SumsFile = 'SHA256SUMS',
    [switch]$SkipSignature,
    [switch]$RequireSignature,
    [string]$CertificateIdentityRegexp = 'https://github\.com/Ranecc/McDl/\.github/workflows/release\.yml@refs/tags/v.*',
    [string]$CertificateOidcIssuer = 'https://token.actions.githubusercontent.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $Directory) { $Directory = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist' }
$Directory = [IO.Path]::GetFullPath($Directory)

$problems = New-Object System.Collections.Generic.List[string]
$checked = 0

function Get-Sha256([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Write-Result([bool]$Ok, [string]$Message) {
    if ($Ok) {
        Write-Host "  [OK]   $Message" -ForegroundColor Green
    }
    else {
        Write-Host "  [FAIL] $Message" -ForegroundColor Red
    }
}

function Test-OneSumFile {
    param([string]$SumsPath)

    if (-not (Test-Path -LiteralPath $SumsPath)) {
        $problems.Add("找不到校验和文件：$SumsPath")
        return
    }
    Write-Host "校验 $SumsPath"
    # 兼容 "hash  name"（GNU 文本模式）与 "hash *name"（二进制模式）。
    $lineNumber = 0
    foreach ($line in (Get-Content -LiteralPath $SumsPath)) {
        $lineNumber++
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) { continue }
        if ($line -notmatch '^(?<hash>[0-9A-Fa-f]{64}) [ *](?<name>.+)$') {
            $problems.Add("第 $lineNumber 行格式无法识别：$line")
            Write-Result $false "第 $lineNumber 行格式无法识别：$line"
            continue
        }
        $expected = $Matches['hash']
        $name = $Matches['name']
        $target = Join-Path (Split-Path -Parent $SumsPath) $name
        if (-not (Test-Path -LiteralPath $target)) {
            $problems.Add("缺少文件：$name")
            Write-Result $false "缺少文件：$name"
            continue
        }
        $actual = Get-Sha256 $target
        $script:checked++
        if ($actual -ieq $expected) {
            Write-Result $true "$name  $actual"
        }
        else {
            $problems.Add("$name 哈希不符：期望 $expected，实际 $actual")
            Write-Result $false "$name 哈希不符`n         期望 $expected`n         实际 $actual"
        }
    }
}

# ---------------------------------------------------------------- 1. SHA256SUMS
$sumsPath = Join-Path $Directory $SumsFile
Test-OneSumFile -SumsPath $sumsPath

# ---------------------------------------------------------------- 2. 单文件 exe 的哈希
Get-ChildItem -LiteralPath $Directory -Filter '*.exe.sha256' -File -ErrorAction SilentlyContinue |
    ForEach-Object {
        if ($_.Name -eq "SHA256SUMS") { return }
        $exeName = $_.Name -replace '\.exe\.sha256$', '.exe'
        $exePath = Join-Path $Directory $exeName
        if (-not (Test-Path -LiteralPath $exePath)) {
            $problems.Add("找不到 $($_.Name) 对应的 $exeName")
            Write-Result $false "找不到 $($_.Name) 对应的 $exeName"
            return
        }
        $line = (Get-Content -LiteralPath $_.FullName | Where-Object { $_ -match '\S' } | Select-Object -First 1)
        if ($line -notmatch '^(?<hash>[0-9A-Fa-f]{64}) [ *](?<name>.+)$') {
            $problems.Add("$($_.Name) 格式无法识别：$line")
            Write-Result $false "$($_.Name) 格式无法识别"
            return
        }
        $actual = Get-Sha256 $exePath
        $script:checked++
        if ($actual -ieq $Matches['hash']) {
            Write-Result $true "$exeName  $actual"
        }
        else {
            $problems.Add("$exeName 哈希不符：期望 $($Matches['hash'])，实际 $actual")
            Write-Result $false "$exeName 哈希不符"
        }
    }

# ---------------------------------------------------------------- 3. cosign 签名
$sigPath = Join-Path $Directory "$SumsFile.sig"
$pemPath = Join-Path $Directory "$SumsFile.pem"
$cosign = Get-Command cosign -ErrorAction SilentlyContinue

Write-Host '验证 SHA256SUMS 的 cosign 签名'
if ($SkipSignature) {
    Write-Host '  [SKIP] 已指定 -SkipSignature' -ForegroundColor Yellow
}
elseif (-not $cosign) {
    $msg = '未检测到 cosign，跳过签名验证（只校验了哈希）。安装：https://docs.sigstore.dev/cosign/installation/'
    if ($RequireSignature) {
        $problems.Add($msg)
        Write-Result $false $msg
    }
    else {
        Write-Host "  [SKIP] $msg" -ForegroundColor Yellow
    }
}
elseif (-not (Test-Path -LiteralPath $sigPath) -or -not (Test-Path -LiteralPath $pemPath)) {
    $msg = "缺少签名文件：$sigPath / $pemPath"
    if ($RequireSignature) {
        $problems.Add($msg)
        Write-Result $false $msg
    }
    else {
        Write-Host "  [SKIP] $msg" -ForegroundColor Yellow
    }
}
else {
    $cosignArgs = @(
        'verify-blob', $sumsPath,
        '--signature', $sigPath,
        '--certificate', $pemPath,
        '--certificate-identity-regexp', $CertificateIdentityRegexp,
        '--certificate-oidc-issuer', $CertificateOidcIssuer
    )
    $output = & cosign @cosignArgs 2>&1
    $ok = $LASTEXITCODE -eq 0
    Write-Result $ok "cosign verify-blob（$SumsFile）"
    foreach ($line in $output) { Write-Host "         $line" -ForegroundColor DarkGray }
    if (-not $ok) { $problems.Add("cosign 签名验证失败：$($output -join ' ')") }
    else { $script:checked++ }
}

# ---------------------------------------------------------------- 汇总
Write-Host ''
if ($problems.Count -gt 0) {
    Write-Host "验证失败：$($problems.Count) 个问题" -ForegroundColor Red
    foreach ($problem in $problems) { Write-Host "  - $problem" -ForegroundColor Red }
    exit 1
}

Write-Host "全部通过（校验了 $checked 项）。" -ForegroundColor Green
exit 0

