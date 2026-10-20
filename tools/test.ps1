# Builds and runs the unit tests with the xUnit console runner.
#
# `dotnet test` is not usable here: the .NET 8+/10 SDK ships a vstest ObjectModel that
# cannot load the xUnit 2.x adapter inside a net48 test host (missing
# Microsoft.Bcl.AsyncInterfaces), so discovery fails with "no tests available".
# The console runner is self contained and gives the same pass/fail signal in CI.
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests\McDl.Tests\McDl.Tests.csproj'

Write-Host "==> dotnet build $project -c $Configuration"
dotnet build $project -c $Configuration --nologo -v minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$locals = dotnet nuget locals global-packages --list
$globalPackages = ($locals -replace '^\s*global-packages:\s*', '').Trim()
$runner = Join-Path $globalPackages 'xunit.runner.console\2.9.3\tools\net48\xunit.console.exe'
if (-not (Test-Path $runner)) {
    throw "xunit.console.exe not found at $runner - run 'dotnet restore tests\McDl.Tests\McDl.Tests.csproj' first."
}

$assembly = Join-Path $root "tests\McDl.Tests\bin\$Configuration\net48\McDl.Tests.dll"
Write-Host "==> xunit.console $assembly"
& $runner $assembly -nologo
exit $LASTEXITCODE
