$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
public static class NativeMouse {
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }
    public static void Down(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
    }
    public static void Move(int x, int y) {
        SetCursorPos(x, y);
    }
    public static void Up() {
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }
    public static void Drag(int x1, int y1, int x2, int y2) {
        SetCursorPos(x1, y1);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);
        for (int step = 1; step <= 10; step++) {
            SetCursorPos(x1 + ((x2 - x1) * step / 10), y1 + ((y2 - y1) * step / 10));
            Thread.Sleep(20);
        }
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);
    }
}
"@

$projectRoot = Split-Path $PSScriptRoot -Parent
$executable = Join-Path $projectRoot 'bin\Release\net8.0-windows\GlassBar.exe'
$evidenceDir = Join-Path $projectRoot '.gstack\qa-reports\screenshots'
$settingsFolder = Join-Path $env:LOCALAPPDATA 'GlassBar'
$settingsPath = Join-Path $settingsFolder 'settings.json'
$hadSettings = Test-Path -LiteralPath $settingsPath
$settingsBackup = if ($hadSettings) { [IO.File]::ReadAllText($settingsPath) } else { $null }
$stickerFixture = Join-Path $projectRoot 'tests\fixtures\qa-sticker.gif'
New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null
New-Item -ItemType Directory -Path $settingsFolder -Force | Out-Null

$qaSettings = [ordered]@{
    HideNativeTaskbar = $false
    StartWithWindows = $false
    Opacity = 0.82
    EffectIntensity = 0.72
    Effect = 'Rain'
    Accent = '#7DD3FC'
    BarWidth = 980
    BarHeight = 68
    CornerRadius = 22
    Stickers = @([ordered]@{
        Id = 'qa-sticker'
        DisplayName = 'QA Sticker'
        FilePath = $stickerFixture
        X = 0.25
        Y = 0.1
        Size = 44
        Opacity = 0.95
    })
}
[IO.File]::WriteAllText($settingsPath, ($qaSettings | ConvertTo-Json -Depth 5))

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

function Test-ElementExists([string]$automationId, [int]$processId) {
    $idCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $condition = New-Object System.Windows.Automation.AndCondition($idCondition, $processCondition)
    $element = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants, $condition)
    return $null -ne $element
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

    $widthBeforeDrag = [Math]::Round($main.Current.BoundingRectangle.Width)
    $sliderRect = $widthSlider.Current.BoundingRectangle
    $sliderFraction = ($range.Current.Value - $range.Current.Minimum) / ($range.Current.Maximum - $range.Current.Minimum)
    $thumbX = [int]($sliderRect.Left + 8 + $sliderFraction * ($sliderRect.Width - 16))
    $thumbY = [int]($sliderRect.Top + $sliderRect.Height / 2)
    [NativeMouse]::Down($thumbX, $thumbY)
    [NativeMouse]::Move(($thumbX + 55), $thumbY)
    Start-Sleep -Milliseconds 280
    $widthDuringDrag = [Math]::Round((Find-Element 'GlassBarMainWindow' $process.Id).Current.BoundingRectangle.Width)
    [NativeMouse]::Up()
    Start-Sleep -Milliseconds 450
    $main = Find-Element 'GlassBarMainWindow' $process.Id
    $widthAfterDrag = [Math]::Round($main.Current.BoundingRectangle.Width)
    $widthStableDuringDrag = $widthDuringDrag -eq $widthBeforeDrag
    $widthAppliedOnRelease = $widthAfterDrag -gt $widthBeforeDrag

    $builtInEffectsSelectable = $true
    foreach ($effectTest in @(
        @{ Id = 'EffectSnow'; Name = 'Snow' },
        @{ Id = 'EffectFireflies'; Name = 'Fireflies' },
        @{ Id = 'EffectPulse'; Name = 'Pulse' }
    )) {
        Invoke-Element (Find-Element $effectTest.Id $process.Id)
        Start-Sleep -Milliseconds 160
        $savedEffect = ([IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json).Effect
        if ($savedEffect -ne $effectTest.Name) { $builtInEffectsSelectable = $false }
    }

    Invoke-Element (Find-Element 'EffectCustom' $process.Id)
    $customPanel = Find-Element 'CustomEffectName' $process.Id
    $customDensity = Find-Element 'CustomDensity' $process.Id
    $customDensity.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(0.85)
    Start-Sleep -Milliseconds 220
    $customSettings = [IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json
    $customEffectSaved = $customSettings.Effect -eq 'Custom' -and
        [Math]::Abs([double]$customSettings.CustomEffect.Density - 0.85) -lt 0.001
    Invoke-Element (Find-Element 'EffectRain' $process.Id)
    Start-Sleep -Milliseconds 220

    $stickerPicker = Find-Element 'StickerPicker' $process.Id
    $stickerX = [int]($main.Current.BoundingRectangle.Left + 4 + (0.25 * ($main.Current.BoundingRectangle.Width - 8 - 44)) + 22)
    $stickerY = [int]($main.Current.BoundingRectangle.Bottom - 68 + (0.1 * (64 - 44)) + 22)
    [NativeMouse]::Drag(
        $stickerX,
        $stickerY,
        ($stickerX + 80),
        $stickerY)
    Start-Sleep -Milliseconds 500
    $savedStickerX = ([IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json).Stickers[0].X

    $mainRect = $main.Current.BoundingRectangle
    [NativeMouse]::Click([int]($mainRect.Left + 40), [int]($mainRect.Top + 40))
    Start-Sleep -Milliseconds 500
    $settingsDismissed = -not (Test-ElementExists 'SettingsPanel' $process.Id)
    $customMenuOpened = $menu.Current.IsEnabled
    $designerOpened = $widthSlider.Current.IsEnabled
    $customEffectEditorOpened = $customPanel.Current.IsEnabled
    $stickerSelectorOpened = $stickerPicker.Current.IsEnabled
    $appliedWindowWidth = [Math]::Round($main.Current.BoundingRectangle.Width)

    Stop-Process -Id $process.Id -Force
    Start-Sleep -Seconds 2
    Get-Process GlassBar -ErrorAction SilentlyContinue |
        Where-Object Path -EQ $executable |
        Stop-Process -Force
    $process = Start-Process -FilePath $executable -ArgumentList '--safe' -PassThru
    Start-Sleep -Seconds 2
    $restartMain = Find-Element 'GlassBarMainWindow' $process.Id
    Invoke-Element (Find-Element 'SettingsButton' $process.Id)
    $restartPicker = Find-Element 'StickerPicker' $process.Id
    $restartSettings = [IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json
    $restartStickerCount = @($restartSettings.Stickers).Count
    $restartStickerX = [double]$restartSettings.Stickers[0].X
    $restartPickerEnabled = $restartPicker.Current.IsEnabled
    $stickerRestored = $restartStickerCount -eq 1 -and
        [Math]::Abs($restartStickerX - [double]$savedStickerX) -lt 0.001 -and
        $restartPickerEnabled

    [pscustomobject]@{
        AppResponsive = $process.Responding
        CustomMenuOpened = $customMenuOpened
        SearchValue = $searchValue
        DesignerOpened = $designerOpened
        BuiltInEffectsSelectable = $builtInEffectsSelectable
        CustomEffectEditorOpened = $customEffectEditorOpened
        CustomEffectSaved = $customEffectSaved
        WidthStableDuringDrag = $widthStableDuringDrag
        WidthAppliedOnRelease = $widthAppliedOnRelease
        StickerSelectorOpened = $stickerSelectorOpened
        StickerPositionSaved = $savedStickerX -gt 0.25
        StickerRestoredAfterRestart = $stickerRestored
        RestartStickerCount = $restartStickerCount
        RestartPosition = [Math]::Round($restartStickerX, 4)
        SettingsDismissedOnBackground = $settingsDismissed
        AppliedWindowWidth = $appliedWindowWidth
        BarScreenshot = $barScreenshot
        MenuScreenshot = $menuScreenshot
        DesignerScreenshot = $designerScreenshot
    } | Format-List
}
finally {
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Start-Sleep -Milliseconds 700
    if ($hadSettings) { [IO.File]::WriteAllText($settingsPath, $settingsBackup) }
    elseif (Test-Path -LiteralPath $settingsPath) { Remove-Item -LiteralPath $settingsPath -Force }
}
