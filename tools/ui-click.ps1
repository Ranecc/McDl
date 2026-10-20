param(
    [string]$WindowTitle = 'McDl',
    [string]$ButtonName = '开始安装'
)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$windowCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, $WindowTitle)
$window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $windowCondition)
if (-not $window) {
    # Fall back to a prefix match over all top-level windows.
    foreach ($candidate in $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
        $name = $candidate.Current.Name
        if ($name -and $name.StartsWith($WindowTitle, [System.StringComparison]::OrdinalIgnoreCase)) { $window = $candidate; break }
    }
}
if (-not $window) { Write-Output 'window-not-found'; exit 1 }

$typeCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
$buttons = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $typeCondition)

$target = $null
foreach ($button in $buttons) {
    if ($button.Current.Name -eq $ButtonName) { $target = $button; break }
}
if (-not $target) {
    Write-Output ("button-not-found; available: " + (($buttons | ForEach-Object { $_.Current.Name }) -join ', '))
    exit 1
}

if (-not $target.Current.IsEnabled) { Write-Output 'button-disabled'; exit 2 }

$pattern = $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$pattern.Invoke()
Write-Output ('clicked: ' + $ButtonName)
