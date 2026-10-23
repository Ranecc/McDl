<#
.SYNOPSIS
    给 McDl 拍一张界面截图（用于 README/docs/自查）。

.DESCRIPTION
    不用 PrintWindow：它在双缓冲窗口上抓到的是上一帧，文字抗锯齿也会丢。
    这里把窗口临时置顶后用 CopyFromScreen 抓屏，得到的才是屏幕上真实的样子。
    桌面被别的窗口挡住也没关系——只要窗口是 TOPMOST 且没人再盖上去。

.PARAMETER OutPath
    PNG 输出路径。

.PARAMETER Theme
    0 = 跟随系统，1 = 浅色，2 = 深色。

.PARAMETER PackPath
    可选，.mrpack/.zip/.hmcl 路径，会作为命令行参数传给 McDl（顺便切到整合包页）。

.PARAMETER ExePath
    默认用 Release 构建产物。

.PARAMETER WaitSeconds
    等界面加载版本列表的时间，默认 45 秒（走 302 代理时清单可能要 40 秒以上）。
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$OutPath,
    [int]$Theme = 1,
[int]$Page = -1,
    [string]$PackPath,
    [string]$ExePath = "E:\DEV\WechatBotWorkspace\mcdl\src\McDl.App\bin\Release\net48\McDl.exe",
    [int]$WaitSeconds = 45
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
public struct RECT { public int Left, Top, Right, Bottom; }
'@ -Name Win -Namespace McDlShot

function Stop-App {
    Get-Process -Name McDl -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 800
}

$settingsDir = Join-Path $env:APPDATA 'McDl'
New-Item -ItemType Directory -Path $settingsDir -Force | Out-Null
$settingsPath = Join-Path $settingsDir 'settings.json'
$backupPath = Join-Path $settingsDir 'settings.shot-backup.json'
$hadSettings = Test-Path $settingsPath
if ($hadSettings) { Copy-Item -LiteralPath $settingsPath -Destination $backupPath -Force }
$json = '{"Root":"E:/DEV/WechatBotWorkspace/mcdl/.tmp-ui/shot","Threads":8,"WithAssets":true,"ThemeMode":' + $Theme + '}'
Set-Content -Path $settingsPath -Value $json -Encoding UTF8

Stop-App

$startArgs = @{
    FilePath               = $ExePath
    RedirectStandardOutput = (Join-Path $env:TEMP 'mcdl-shot-out.txt')
    RedirectStandardError  = (Join-Path $env:TEMP 'mcdl-shot-err.txt')
}
if ($PackPath) { $startArgs['ArgumentList'] = '"' + $PackPath + '"' }
Start-Process @startArgs

Start-Sleep -Seconds $WaitSeconds

$process = Get-Process -Name McDl -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $process) {
    throw 'McDl 没在运行，检查 %TEMP%\mcdl-shot-err.txt'
}

$handle = $process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) {
    throw 'McDl 没有主窗口句柄'
}

# 置顶 + 挪到左上角，保证抓屏区域不被任务栏/其他窗口覆盖。
[McDlShot.Win]::SetWindowPos($handle, [IntPtr](-1), 10, 10, 0, 0, 0x0001 -bor 0x0010) | Out-Null
Start-Sleep -Milliseconds 400
if ($Page -ge 0) {
  # 左侧导航项：x 固定 88（视窗 10 + 78），首项 y≈100，行距 48
  $navY = 10 + 100 + ($Page * 48)
  [McDlShot.Win]::SetCursorPos(88, $navY) | Out-Null
  [McDlShot.Win]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
  [McDlShot.Win]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
  Start-Sleep -Seconds 2
}
Start-Sleep -Milliseconds 1500

$rect = New-Object McDlShot.Win+RECT
[McDlShot.Win]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { throw "窗口尺寸异常: ${width}x${height}" }

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
try {
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))
    $full = [System.IO.Path]::GetFullPath($OutPath)
    New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($full)) -Force | Out-Null
    $bitmap.Save($full, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
  if ($hadSettings -and (Test-Path $backupPath)) { Copy-Item -LiteralPath $backupPath -Destination $settingsPath -Force; Remove-Item -LiteralPath $backupPath -Force }
  elseif (-not $hadSettings) { Remove-Item -LiteralPath $settingsPath -Force -ErrorAction SilentlyContinue }
    $graphics.Dispose()
    $bitmap.Dispose()
    Stop-App
}

"captured: $OutPath (${width}x${height})"
