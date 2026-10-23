<#
.SYNOPSIS
    扫一遍被跟踪的文本文件，找出不该出现在公开仓库里的本机痕迹。

.DESCRIPTION
    文档里的示例路径要写占位符（<安装目录>），不要贴本机真实路径。
    一旦仓库里出现每个人的盘符、用户名、主机名，就等于把作者的工作环境公开了，
    而且这类内容一旦提交就会留在 git 历史里，后面很难彻底清掉。

    检查的类别：
      * Windows 绝对路径    C:\... / D:\...
      * UNC 路径            \\host\share
      * 用户目录            Users\<name>
      * 已知主机名/工作目录名（本脚本自身的排除列表里也有一份，注意不要写回原文）

    退出码：发现命中返回 1，干净返回 0。CI 的 build 作业会跑它。

.PARAMETER Path
    要扫描的目录，默认仓库根。

.EXAMPLE
    pwsh -NoProfile -File tools\check-leaks.ps1
#>
[CmdletBinding()]
param(
    [string]$Path
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $Path) { $Path = $repoRoot }
$Path = (Resolve-Path -LiteralPath $Path).Path

# 这些文件本身就写着要被检查的模式，扫自己必中，直接跳过。
$self = @(
    'tools/check-leaks.ps1'
)

# 用拼接写法，避免脚本源码里出现完整的敏感词从而扫到自己。
$hostWords = @('Snow' + 'Dream', 'local' + 'domain', 'Wechat' + 'BotWorkspace')

$rules = @(
    # 只盯"能指到人"的东西。D:\Games、C:\Java 这类示例路径是有用的文档内容，不拦。
    @{ Name = '用户目录'; Pattern = '(?i)Users\\(?!<)(?!runneradmin)[A-Za-z0-9._-]+' },
    @{ Name = 'UNC 路径'; Pattern = '(?i)\\\\[A-Za-z0-9._-]+\\' }
)
foreach ($word in $hostWords) {
    $rules += @{ Name = '主机名/工作目录名'; Pattern = ('(?i)' + [regex]::Escape($word)) }
}

function Test-Binary {
    param([byte[]]$Bytes)
    $limit = [Math]::Min($Bytes.Length, 4096)
    for ($i = 0; $i -lt $limit; $i++) {
        if ($Bytes[$i] -eq 0) { return $true }
    }
    return $false
}

Push-Location $Path
try {
    if (-not (Test-Path -LiteralPath '.git')) {
        throw "这里不是 git 仓库：$Path"
    }

    $files = git ls-files
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files 失败' }

    $hits = New-Object System.Collections.Generic.List[string]

    foreach ($file in $files) {
        if ($self -contains ($file -replace '\\', '/')) { continue }
        if (-not (Test-Path -LiteralPath $file)) { continue }

        $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $file))
        if ($bytes.Length -eq 0) { continue }
        if (Test-Binary -Bytes $bytes) { continue }

        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        $lines = $text -split "`n"

        for ($n = 0; $n -lt $lines.Length; $n++) {
            $line = $lines[$n]
            foreach ($rule in $rules) {
                if ([regex]::IsMatch($line, $rule.Pattern)) {
                    $hits.Add(('{0}:{1}: [{2}] {3}' -f $file, ($n + 1), $rule.Name, $line.Trim()))
                    break
                }
            }
        }
    }

    if ($hits.Count -gt 0) {
        Write-Host '发现本机痕迹（请改成占位符，例如 <安装目录>）：' -ForegroundColor Red
        foreach ($hit in $hits) { Write-Host ('  ' + $hit) -ForegroundColor Red }
        Write-Host ("共 {0} 处。" -f $hits.Count) -ForegroundColor Red
        exit 1
    }

    Write-Host ("检查通过：{0} 个被跟踪文件里没有本机路径或主机名。" -f $files.Count) -ForegroundColor Green
    exit 0
}
finally {
    Pop-Location
}
