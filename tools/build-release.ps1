param(
    [string]$Version = '0.1.0',
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'

if (-not $SkipBuild) {
    Write-Host '==> building Core / Cli / App (Release)' -ForegroundColor Cyan
    dotnet build (Join-Path $root 'src\McDl.App\McDl.App.csproj') -c Release -v q | Out-Null
    dotnet build (Join-Path $root 'src\McDl.Cli\McDl.Cli.csproj') -c Release -v q | Out-Null
}

$appExe = Join-Path $root 'src\McDl.App\bin\Release\net48\McDl.exe'
if (-not (Test-Path $appExe)) { throw "missing build output: $appExe" }

$stage = Join-Path $dist "stage-$Version"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Copy-Item $appExe (Join-Path $stage 'McDl.exe') -Force
Copy-Item (Join-Path $root 'README.md') (Join-Path $stage 'README.md') -Force
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $stage 'LICENSE.txt') -Force

$zip = Join-Path $dist "McDl-$Version-win.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
$size = (Get-Item $zip).Length
Write-Host ''
Write-Host ("package : {0}" -f $zip) -ForegroundColor Green
Write-Host ("size    : {0:N0} bytes ({1:N1} KB)" -f $size, ($size / 1KB))
Write-Host ("sha256  : {0}" -f $hash)
