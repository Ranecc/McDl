# Opens a .mrpack through the GUI's own file dialog using UI Automation.
# Usage: pwsh -NoProfile -File tools/ui-modpack.ps1 -Path <file.mrpack>
param(
    [Parameter(Mandatory = $true)][string]$Path
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]

function Find-ProcessWindow([string]$namePrefix) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, (Get-Process -Name $namePrefix -ErrorAction Stop).Id)
    return $AE::RootElement.FindFirst($TS::Children, $condition)
}

function Find-ByName($root, $controlType, [string]$namePattern) {
    $condition = New-Object System.Windows.Automation.AndCondition(
        (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $controlType)),
        (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $namePattern))
    )
    return $root.FindFirst($TS::Descendants, $condition)
}

# Themed flat buttons surface as ControlType.Pane with the caption as Name, so match on the
# name alone and then look for something invokable.
# Our flat buttons expose no UIA patterns, so fall back to a real mouse click in the middle
# of the element's bounding rectangle.
function Click-Element($element) {
    Add-Type -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
[DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
public const uint DOWN = 0x0002;
public const uint UP = 0x0004;
public const uint BM_CLICK = 0x00F5;
'@ -Name NativeMouse -Namespace Win32

    # Post (not Send): the click opens a modal dialog, and a synchronous SendMessage would
    # block this script until that dialog closes.
    $hwnd = [IntPtr]$element.Current.NativeWindowHandle
    if ($hwnd -ne [IntPtr]::Zero) {
        [Win32.NativeMouse]::PostMessage($hwnd, [Win32.NativeMouse]::BM_CLICK, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        return
    }

    $rect = $element.Current.BoundingRectangle
    $x = [int]($rect.X + $rect.Width / 2)
    $y = [int]($rect.Y + $rect.Height / 2)
    [Win32.NativeMouse]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 120
    [Win32.NativeMouse]::mouse_event([Win32.NativeMouse]::DOWN, 0, 0, 0, [UIntPtr]::Zero)
    [Win32.NativeMouse]::mouse_event([Win32.NativeMouse]::UP, 0, 0, 0, [UIntPtr]::Zero)
}

function Invoke-ByName($root, [string]$name) {
    $condition = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    $element = $root.FindFirst($TS::Descendants, $condition)
    if ($null -eq $element) { return $false }

    foreach ($pattern in $element.GetSupportedPatterns()) {
        if ($pattern -eq [System.Windows.Automation.InvokePattern]::Pattern) {
            $element.GetCurrentPattern($pattern).Invoke()
            return $true
        }
    }

    Click-Element $element
    return $true
}

$window = Find-ProcessWindow 'McDl'
if ($null -eq $window) { throw 'McDl window not found' }

if (-not (Invoke-ByName $window '整合包…')) { throw 'pack button not found' }
Start-Sleep -Milliseconds 1200

# The open dialog is a top-level window of the same process.
$dialogCondition = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, (Get-Process -Name 'McDl').Id)),
    (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Window)),
    (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, '选择 Modrinth 整合包'))
)
$dialog = $AE::RootElement.FindFirst($TS::Children, $dialogCondition)
if ($null -eq $dialog) { throw 'file dialog not found' }

# The file-name edit is the only Edit control in the dialog chrome we care about;
# pick the one that supports ValuePattern and is not read-only.
$editCondition = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, $CT::Edit)
$edits = $dialog.FindAll($TS::Descendants, $editCondition)
$target = $null
foreach ($edit in $edits) {
    $pattern = $null
    if ($edit.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
        if (-not $pattern.Current.IsReadOnly) { $target = $edit; break }
    }
}
if ($null -eq $target) { throw 'file name box not found' }

$target.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Path)
Start-Sleep -Milliseconds 300

$open = Find-ByName $dialog $CT::Button '打开(&O)'
if ($null -eq $open) { $open = Find-ByName $dialog $CT::Button '打开(O)' }
if ($null -eq $open) { $open = Find-ByName $dialog $CT::Button '打开' }
if ($null -eq $open) { throw 'open button not found' }

$open.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 1500
Write-Host "pack selected: $Path"
