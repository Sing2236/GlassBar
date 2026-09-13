$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

$projectRoot = Split-Path $PSScriptRoot -Parent
$executable = Join-Path $projectRoot 'bin\Release\net8.0-windows\GlassBar.exe'
$evidenceDir = Join-Path $projectRoot '.gstack\qa-reports\screenshots'
New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null

function Find-Element([string]$automationId, [int]$processId, [int]$timeoutSeconds = 8) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    $idCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $condition = New-Object System.Windows.Automation.AndCondition($idCondition, $processCondition)

    do {
        $element = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "UI element '$automationId' did not appear within $timeoutSeconds seconds."
}

function Invoke-Element($element) {
    $pattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Capture-Element($element, [string]$name) {
    $rect = $element.Current.BoundingRectangle
    $left = [Math]::Max(0, [int][Math]::Floor($rect.Left) - 18)
    $top = [Math]::Max(0, [int][Math]::Floor($rect.Top) - 18)
    $width = [int][Math]::Ceiling($rect.Width) + 36
    $height = [int][Math]::Ceiling($rect.Height) + 36
    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($left, $top, 0, 0, $bitmap.Size)
        $path = Join-Path $evidenceDir $name
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        return $path
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

$process = Start-Process -FilePath $executable -ArgumentList '--safe' -PassThru
try {
    Start-Sleep -Seconds 2
    $main = Find-Element 'GlassBarMainWindow' $process.Id
    $barScreenshot = Capture-Element $main 'bar-fixed.png'

    Invoke-Element (Find-Element 'StartButton' $process.Id)
    $menu = Find-Element 'GlassBarStartMenu' $process.Id
    $menuScreenshot = Capture-Element $menu 'custom-start-menu.png'

    $search = Find-Element 'StartMenuSearch' $process.Id
    $valuePattern = $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $valuePattern.SetValue('Visual Studio Code')
    Start-Sleep -Milliseconds 500
    $searchValue = $valuePattern.Current.Value

    Invoke-Element (Find-Element 'SettingsButton' $process.Id)
    $widthSlider = Find-Element 'WidthSlider' $process.Id
    $range = $widthSlider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    $range.SetValue(840)
    Start-Sleep -Milliseconds 500
    $main = Find-Element 'GlassBarMainWindow' $process.Id
    $designerScreenshot = Capture-Element $main 'customizer.png'

    [pscustomobject]@{
        AppResponsive = $process.Responding
        CustomMenuOpened = $menu.Current.IsEnabled
        SearchValue = $searchValue
        DesignerOpened = $widthSlider.Current.IsEnabled
        AppliedWindowWidth = [Math]::Round($main.Current.BoundingRectangle.Width)
        BarScreenshot = $barScreenshot
        MenuScreenshot = $menuScreenshot
        DesignerScreenshot = $designerScreenshot
    } | Format-List
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Start-Sleep -Milliseconds 700
}
