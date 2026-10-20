param(
    [string]$WindowTitle = 'McDl'
)

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$window = $null
foreach ($candidate in $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
    $name = $candidate.Current.Name
    if ($name -and $name.StartsWith($WindowTitle, [System.StringComparison]::OrdinalIgnoreCase)) { $window = $candidate; break }
}
if (-not $window) { Write-Output 'window-not-found'; exit 1 }

$condition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Text)

$texts = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
foreach ($item in $texts) {
    $value = $item.Current.Name
    if ($value) { Write-Output $value }
}
