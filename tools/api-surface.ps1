# Generates docs/api-surface.md from the public surface of src/McDl.Core.
#
# Only public types and members are listed. Descriptions come from the XML doc
# comments directly above a declaration; declarations without docs are kept but
# their description is left empty. The output is stable for an unchanged tree,
# so the script only rewrites the file when the content actually changes.
#
# Usage:
#   pwsh -NoProfile -File tools/api-surface.ps1
#   pwsh -NoProfile -File tools/api-surface.ps1 -Output docs/api-surface.md

param(
    [string]$Source = 'src\McDl.Core',
    [string]$Output = 'docs/api-surface.md'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $root $Source
$outputPath = Join-Path $root $Output

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "source directory not found: $sourcePath"
}

function ConvertTo-Text([string[]]$lines) {
    if (-not $lines -or $lines.Count -eq 0) { return '' }

    $clean = foreach ($line in $lines) { $line -replace '^\s*///+\s?', '' }
    $text = ($clean -join ' ')

    if ($text -match '(?s)<summary>(.*?)</summary>') {
        $text = $Matches[1]
    } else {
        $text = $text -replace '(?s)<remarks>.*?</remarks>', ''
    }

    $text = $text -replace '<see cref="([^"]+)"\s*/>', '$1'
    $text = $text -replace '<see langword="([^"]+)"\s*/>', '$1'
    $text = $text -replace '<paramref name="([^"]+)"\s*/>', '$1'
    $text = $text -replace '</?summary>', ''
    $text = $text -replace '</?remarks>', ''
    $text = $text -replace '</?param[^>]*>', ''
    $text = $text -replace '</?returns>', ''
    $text = $text -replace '<[^>]+>', ''
    $text = $text.Replace('&lt;', '<').Replace('&gt;', '>').Replace('&amp;', '&')
    $text = ($text -replace '\s+', ' ').Trim()
    if ($text.Length -eq 0) { return '' }

    $stop = $text.IndexOf('。')
    if ($stop -ge 0) { $text = $text.Substring(0, $stop + 1) }
    if ($text.Length -gt 140) { $text = $text.Substring(0, 137).TrimEnd() + '...' }
    return $text.Replace('|', '\|')
}

function Get-DeclarationKind([string]$line) {
    if ($line -match '^\s*public\s+(?:(?:sealed|abstract|static|partial|readonly)\s+)*(class|interface|enum|struct|record)\s+([A-Za-z_][A-Za-z0-9_]*)') {
        return @{ Kind = 'type'; Name = $Matches[2]; Form = $Matches[1] }
    }
    return $null
}

function Test-PublicMember([string]$line) {
    if ($line -notmatch '^\s*public\s+') { return $false }
    if ($line -match '\b(class|interface|enum|struct|record)\s+[A-Za-z_]') { return $false }
    if ($line -match '^\s*public\s+override\s+') { return $false }
    return $true
}

function Get-BraceDelta([string]$line) {
    # String literals and line comments can contain braces; they do not change
    # the nesting level we care about.
    $clean = $line -replace '"(?:\\.|[^"\\])*"', '""'
    $clean = $clean -replace '//.*$', ''
    $open = ([regex]::Matches($clean, '\{')).Count
    $close = ([regex]::Matches($clean, '\}')).Count
    return $open - $close
}

$files = Get-ChildItem -LiteralPath $sourcePath -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Sort-Object FullName

$rows = New-Object System.Collections.Generic.List[object]
$seen = New-Object 'System.Collections.Generic.HashSet[string]'
$namespace = ''
$depth = 0
$namespaceDepth = -1
$currentType = $null
$typeDepth = -1
$pendingNamespace = $null
$pendingType = $null
$pendingTypeSummary = @()
$summary = @()
$memberLines = @()
$collectingMember = $false

foreach ($file in $files) {
    $lines = [System.IO.File]::ReadAllLines($file.FullName, [System.Text.Encoding]::UTF8)
    $depth = 0
    $namespaceDepth = -1
    $currentType = $null
    $typeDepth = -1
    $pendingType = $null
    $pendingTypeSummary = @()
    $summary = @()
    $memberLines = @()
    $collectingMember = $false

    foreach ($line in $lines) {
        $trim = $line.Trim()

        if ($trim.StartsWith('///')) {
            $summary += $trim
            continue
        }

        if ($collectingMember) {
            $memberLines += $trim
            if ($trim -match '[\{;]' -or $trim -match '=>') {
                $text = ($memberLines -join ' ') -replace '\s+', ' '
                $text = $text -replace '\{.*$', ''
                $text = $text -replace ';.*$', ''
                $text = $text -replace '=>.*$', ''
                $text = $text.Trim().TrimEnd(',')

                if ($text.Length -gt 0 -and $currentType -ne $null) {
                    $key = $currentType.Namespace + '|' + $currentType.Name + '|' + $text
                    if ($seen.Add($key)) {
                        $rows.Add([pscustomobject]@{
                            Namespace   = $currentType.Namespace
                            Type        = $currentType.Name
                            Member      = $text.Replace('|', '\|')
                            Description = (ConvertTo-Text $summary)
                        })
                    }
                }

                $memberLines = @()
                $collectingMember = $false
                $summary = @()
            }
            $depth += Get-BraceDelta $line
            if ($currentType -ne $null -and $depth -lt $typeDepth) { $currentType = $null }
            continue
        }

        if ($pendingNamespace -ne $null -and $trim -eq '{') {
            $namespace = $pendingNamespace
            $namespaceDepth = $depth + 1
            $pendingNamespace = $null
            $depth += 1
            $summary = @()
            continue
        }

        if ($pendingType -ne $null -and $trim.Contains('{')) {
            $currentType = @{ Name = $pendingType.Name; Namespace = $namespace; Form = $pendingType.Form }
            $typeDepth = $depth + 1
            $rows.Add([pscustomobject]@{
                Namespace   = $namespace
                Type        = $pendingType.Name
                Member      = '(' + $pendingType.Form + ')'
                Description = (ConvertTo-Text $pendingTypeSummary)
            })
            $seen.Add($namespace + '|' + $pendingType.Name + '|(type)') | Out-Null
            $pendingType = $null
            $pendingTypeSummary = @()
            $depth += Get-BraceDelta $line
            $summary = @()
            continue
        }

        if ($trim -match '^namespace\s+([A-Za-z0-9_.]+)') {
            $pendingNamespace = $Matches[1]
            $summary = @()
            continue
        }

        if ($currentType -eq $null) {
            $decl = Get-DeclarationKind $line
            if ($decl -ne $null) {
                $pendingType = $decl
                $pendingTypeSummary = $summary
                $summary = @()
                $depth += Get-BraceDelta $line
                continue
            }
        }

        if ($currentType -ne $null -and $depth -eq $typeDepth -and (Test-PublicMember $line)) {
            $collectingMember = $true
            $memberLines = @($trim)
            if ($trim -match '[\{;]' -or $trim -match '=>') {
                # Single-line declaration: reuse the collection branch on the next pass.
                $collectingMember = $false
                $text = $trim -replace '\s+', ' '
                $text = $text -replace '\{.*$', ''
                $text = $text -replace ';.*$', ''
                $text = $text -replace '=>.*$', ''
                $text = $text.Trim().TrimEnd(' ')
                if ($text.Length -gt 0) {
                    $key = $currentType.Namespace + '|' + $currentType.Name + '|' + $text
                    if ($seen.Add($key)) {
                        $rows.Add([pscustomobject]@{
                            Namespace   = $currentType.Namespace
                            Type        = $currentType.Name
                            Member      = $text.Replace('|', '\|')
                            Description = (ConvertTo-Text $summary)
                        })
                    }
                }
                $memberLines = @()
                $summary = @()
            }
            $depth += Get-BraceDelta $line
            if ($currentType -ne $null -and $depth -lt $typeDepth) { $currentType = $null }
            continue
        }

        if ($trim -ne '' -and -not $trim.StartsWith('[')) { $summary = @() }
        $depth += Get-BraceDelta $line
        if ($currentType -ne $null -and $depth -lt $typeDepth) { $currentType = $null }
    }
}

$relativeSource = $Source -replace '\\', '/'
$relativeOutput = $Output -replace '\\', '/'
$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine('# McDl.Core API 一览')
[void]$builder.AppendLine()
[void]$builder.AppendLine('> 由脚本生成，勿手工编辑。生成命令：`pwsh -NoProfile -File tools/api-surface.ps1`')
[void]$builder.AppendLine('>')
[void]$builder.AppendLine('> 只列 `public` 类型与成员，说明取自源码里的 XML 注释；没有注释的留空。目录是 `' + $relativeSource + '`。')
[void]$builder.AppendLine()
[void]$builder.Append('共 ').Append($rows.Count).AppendLine(' 行（类型行 + 成员行）。')
[void]$builder.AppendLine()
[void]$builder.AppendLine('| 命名空间 | 类型 | 成员 | 说明 |')
[void]$builder.AppendLine('| --- | --- | --- | --- |')

foreach ($row in $rows) {
    [void]$builder.Append('| ').Append($row.Namespace).Append(' | ').Append($row.Type).Append(' | ')
    [void]$builder.Append($row.Member).Append(' | ').Append($row.Description).AppendLine(' |')
}

$content = $builder.ToString().Replace("`r`n", "`n").Replace("`n", "`r`n")
$encoding = New-Object System.Text.UTF8Encoding($false)

$existing = $null
if (Test-Path -LiteralPath $outputPath) {
    $existing = [System.IO.File]::ReadAllText($outputPath, [System.Text.Encoding]::UTF8)
}

if ($existing -eq $content) {
    Write-Host "unchanged: $relativeOutput ($($rows.Count) rows)"
} else {
    $dir = Split-Path -Parent $outputPath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
    [System.IO.File]::WriteAllText($outputPath, $content, $encoding)
    Write-Host "wrote: $relativeOutput ($($rows.Count) rows)"
}
