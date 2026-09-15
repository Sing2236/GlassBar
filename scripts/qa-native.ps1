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
$installedExecutable = Join-Path $env:LOCALAPPDATA 'Programs\GlassBar\GlassBar.exe'
$installedWasRunning = @(Get-Process GlassBar -ErrorAction SilentlyContinue |
    Where-Object Path -EQ $installedExecutable).Count -gt 0
$evidenceDir = Join-Path $projectRoot '.gstack\qa-reports\screenshots'
$settingsFolder = Join-Path $env:LOCALAPPDATA 'GlassBar'
$settingsPath = Join-Path $settingsFolder 'settings.json'
$licensePath = Join-Path $settingsFolder 'license.json'
$hadSettings = Test-Path -LiteralPath $settingsPath
$settingsBackup = if ($hadSettings) { [IO.File]::ReadAllText($settingsPath) } else { $null }
$hadLicense = Test-Path -LiteralPath $licensePath
$licenseBackup = if ($hadLicense) { [IO.File]::ReadAllText($licensePath) } else { $null }
$privateKeyPath = Join-Path $env:USERPROFILE '.glassbar\license-private.pem'
$issuerProject = Join-Path $projectRoot 'tools\GlassBar.LicenseIssuer\GlassBar.LicenseIssuer.csproj'
$stickerFixture = Join-Path $projectRoot 'tests\fixtures\qa-sticker.gif'
New-Item -ItemType Directory -Path $evidenceDir -Force | Out-Null
New-Item -ItemType Directory -Path $settingsFolder -Force | Out-Null

$qaSettings = [ordered]@{
    HideNativeTaskbar = $false
    StartWithWindows = $false
    UseWindowsSearch = $false
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
if (Test-Path -LiteralPath $licensePath) { Remove-Item -LiteralPath $licensePath -Force }
if (-not (Test-Path -LiteralPath $privateKeyPath)) {
    throw "The local QA signing key was not found at $privateKeyPath."
}
$issuerOutput = @(& dotnet run --project $issuerProject -c Release -- issue $privateKeyPath 'qa@glassbar.local')
$qaLicenseKey = ($issuerOutput | Where-Object { $_ -like 'GB1.*' } | Select-Object -Last 1).Trim()
if ([string]::IsNullOrWhiteSpace($qaLicenseKey)) { throw 'The QA license issuer did not return a key.' }

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

function Wait-SelectedResult([string]$expectedName, [int]$processId, [bool]$requireIcon = $false, [int]$timeoutSeconds = 8) {
    $deadline = [DateTime]::UtcNow.AddSeconds($timeoutSeconds)
    $results = Find-Element 'StartMenuResults' $processId $timeoutSeconds
    $selectionPattern = $results.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern)

    do {
        $selection = @($selectionPattern.Current.GetSelection())
        if ($selection.Count -gt 0) {
            $selectedName = $selection[0].Current.Name
            if ([string]$selectedName -like "*$expectedName*") {
                if ($requireIcon -and $selection[0].Current.HelpText -ne 'Windows icon') {
                    throw "'$expectedName' rendered GlassBar's fallback glyph instead of its Windows app icon."
                }
                return $true
            }
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "'$expectedName' was not selected as the best search result within $timeoutSeconds seconds."
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

$process = $null
if ($installedWasRunning) {
    Get-Process GlassBar -ErrorAction SilentlyContinue |
        Where-Object Path -EQ $installedExecutable |
        Stop-Process -Force
    Start-Sleep -Seconds 2
}
$process = Start-Process -FilePath $executable -ArgumentList @('--safe', '--qa-visible') -PassThru
try {
    Start-Sleep -Seconds 2
    $main = Find-Element 'GlassBarMainWindow' $process.Id
    $barScreenshot = Capture-Element $main 'bar-fixed.png'

    Invoke-Element (Find-Element 'StartButton' $process.Id)
    $menu = Find-Element 'GlassBarStartMenu' $process.Id
    $menuScreenshot = Capture-Element $menu 'custom-start-menu.png'
    Start-Sleep -Milliseconds 450
    $menuAnimatedScreenshot = Capture-Element $menu 'custom-start-menu-animated.png'
    $startMenuEffectAnimated = (Get-FileHash $menuScreenshot -Algorithm SHA256).Hash -ne
        (Get-FileHash $menuAnimatedScreenshot -Algorithm SHA256).Hash

    $search = Find-Element 'StartMenuSearch' $process.Id
    $search.SetFocus()
    $valuePattern = $search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $valuePattern.SetValue('Visual Studio Code')
    Start-Sleep -Milliseconds 500
    $searchValue = $valuePattern.Current.Value
    $valuePattern.SetValue('Calculator')
    $calculatorSearchFound = Wait-SelectedResult 'Calculator' $process.Id $true
    $calculatorScreenshot = Capture-Element $menu 'calculator-search.png'
    $valuePattern.SetValue('Bluetooth')
    $settingsSearchFound = Wait-SelectedResult 'Bluetooth & devices' $process.Id

    Invoke-Element (Find-Element 'SettingsButton' $process.Id)
    $widthSlider = Find-Element 'WidthSlider' $process.Id
    $range = $widthSlider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
    $range.SetValue(840)
    Start-Sleep -Milliseconds 500
    $main = Find-Element 'GlassBarMainWindow' $process.Id
    $designerScreenshot = Capture-Element $main 'customizer.png'

    $windowsSearchToggle = Find-Element 'UseWindowsSearch' $process.Id
    $windowsSearchTogglePattern = $windowsSearchToggle.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    $windowsSearchTogglePattern.Toggle()
    Start-Sleep -Milliseconds 180
    $windowsSearchPreferenceSaved = ([IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json).UseWindowsSearch -eq $true
    $windowsSearchTogglePattern.Toggle()
    Start-Sleep -Milliseconds 180

    Invoke-Element (Find-Element 'EffectAurora' $process.Id)
    Start-Sleep -Milliseconds 180
    $auroraIsFree = ([IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json).Effect -eq 'Aurora'
    Invoke-Element (Find-Element 'EffectRain' $process.Id)
    Start-Sleep -Milliseconds 180
    $rainIsFree = ([IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json).Effect -eq 'Rain'

    Invoke-Element (Find-Element 'EffectSnow' $process.Id)
    Start-Sleep -Milliseconds 180
    $lockedEffect = ([IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json).Effect
    $licenseMessage = Find-Element 'LicenseMessage' $process.Id
    $premiumLockedWithoutKey = $lockedEffect -eq 'Rain' -and $licenseMessage.Current.Name -like '*requires the $5*'

    $licenseBox = Find-Element 'LicenseKey' $process.Id
    $licenseBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($qaLicenseKey.Substring(0, $qaLicenseKey.Length - 1) + 'x')
    Invoke-Element (Find-Element 'ActivateLicense' $process.Id)
    Start-Sleep -Milliseconds 180
    $invalidKeyRejected = -not (Test-Path -LiteralPath $licensePath) -and
        (Find-Element 'LicenseMessage' $process.Id).Current.Name -like '*not valid*'

    $licenseBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($qaLicenseKey)
    Invoke-Element (Find-Element 'ActivateLicense' $process.Id)
    Start-Sleep -Milliseconds 250
    $proStatus = Find-Element 'ProStatus' $process.Id
    $licenseActivated = $proStatus.Current.Name -eq 'UNLOCKED' -and (Test-Path -LiteralPath $licensePath)

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
    $process = Start-Process -FilePath $executable -ArgumentList @('--safe', '--qa-visible') -PassThru
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
    $restartProStatus = Find-Element 'ProStatus' $process.Id
    $licenseRestored = $restartProStatus.Current.Name -eq 'UNLOCKED'

    [pscustomobject]@{
        AppResponsive = $process.Responding
        CustomMenuOpened = $customMenuOpened
        StartMenuEffectAnimated = $startMenuEffectAnimated
        WindowsSearchPreferenceSaved = $windowsSearchPreferenceSaved
        SearchValue = $searchValue
        CalculatorSearchFound = $calculatorSearchFound
        CalculatorScreenshot = $calculatorScreenshot
        SettingsSearchFound = $settingsSearchFound
        DesignerOpened = $designerOpened
        RainIsFree = $rainIsFree
        AuroraIsFree = $auroraIsFree
        PremiumLockedWithoutKey = $premiumLockedWithoutKey
        InvalidKeyRejected = $invalidKeyRejected
        LicenseActivated = $licenseActivated
        LicenseRestoredAfterRestart = $licenseRestored
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
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Start-Sleep -Milliseconds 700
    if ($hadSettings) { [IO.File]::WriteAllText($settingsPath, $settingsBackup) }
    elseif (Test-Path -LiteralPath $settingsPath) { Remove-Item -LiteralPath $settingsPath -Force }
    if ($hadLicense) { [IO.File]::WriteAllText($licensePath, $licenseBackup) }
    elseif (Test-Path -LiteralPath $licensePath) { Remove-Item -LiteralPath $licensePath -Force }
    if ($installedWasRunning -and (Test-Path -LiteralPath $installedExecutable)) {
        Start-Process -FilePath $installedExecutable | Out-Null
    }
}
