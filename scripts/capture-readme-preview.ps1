$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

$projectRoot = Split-Path $PSScriptRoot -Parent
$executable = Join-Path $projectRoot 'bin\Release\net8.0-windows\GlassBar.exe'
$outputDirectory = Join-Path $projectRoot 'docs'
$outputPath = Join-Path $outputDirectory 'glassbar-effects.gif'
$frameDirectory = Join-Path $projectRoot '.gstack\readme-preview-frames'
$auditDirectory = Join-Path $projectRoot '.gstack\design-reports'
$settingsFolder = Join-Path $env:LOCALAPPDATA 'GlassBar'
$settingsPath = Join-Path $settingsFolder 'settings.json'
$installedExecutable = Join-Path $env:LOCALAPPDATA 'Programs\GlassBar\GlassBar.exe'
$wasInstalledRunning = @(Get-Process GlassBar -ErrorAction SilentlyContinue | Where-Object Path -EQ $installedExecutable).Count -gt 0
$hadSettings = Test-Path -LiteralPath $settingsPath
$settingsBackup = if ($hadSettings) { [IO.File]::ReadAllText($settingsPath) } else { $null }

New-Item -ItemType Directory -Path $outputDirectory, $frameDirectory, $auditDirectory, $settingsFolder -Force | Out-Null

$previewSettings = [ordered]@{
    HideNativeTaskbar = $false
    StartWithWindows = $false
    Opacity = 0.9
    EffectIntensity = 1
    Effect = 'Aurora'
    Accent = '#7DD3FC'
    BarWidth = 980
    BarHeight = 68
    CornerRadius = 22
    CustomEffect = [ordered]@{
        Name = 'Stardrift'
        Shape = 'Spark'
        Motion = 'Drift'
        SecondaryColor = '#A78BFA'
        Density = 0.72
        Speed = 0.55
        Size = 0.5
        Glow = 0.7
        Trail = 0.35
    }
    Stickers = @()
}

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
        Start-Sleep -Milliseconds 120
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "UI element '$automationId' did not appear."
}

function Invoke-Element($element) {
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

function Capture-Window($element, [string]$path) {
    $rect = $element.Current.BoundingRectangle
    $bitmap = New-Object System.Drawing.Bitmap([int][Math]::Ceiling($rect.Width), [int][Math]::Ceiling($rect.Height))
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$rect.Left, [int]$rect.Top, 0, 0, $bitmap.Size)
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
}

Get-Process GlassBar -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1
[IO.File]::WriteAllText($settingsPath, ($previewSettings | ConvertTo-Json -Depth 6))

$process = Start-Process -FilePath $executable -ArgumentList '--safe' -PassThru
try {
    Start-Sleep -Seconds 2
    $main = Find-Element 'GlassBarMainWindow' $process.Id

    for ($index = 0; $index -lt 22; $index++) {
        Capture-Window $main (Join-Path $frameDirectory ('aurora-{0:D2}.png' -f $index))
        Start-Sleep -Milliseconds 85
    }

    Invoke-Element (Find-Element 'SettingsButton' $process.Id)
    Invoke-Element (Find-Element 'EffectRain' $process.Id)
    Invoke-Element (Find-Element 'CloseSettings' $process.Id)
    Start-Sleep -Milliseconds 350
    $main = Find-Element 'GlassBarMainWindow' $process.Id

    for ($index = 0; $index -lt 22; $index++) {
        Capture-Window $main (Join-Path $frameDirectory ('rain-{0:D2}.png' -f $index))
        Start-Sleep -Milliseconds 85
    }

    Invoke-Element (Find-Element 'SettingsButton' $process.Id)
    Invoke-Element (Find-Element 'EffectCustom' $process.Id)
    Start-Sleep -Milliseconds 400
    $main = Find-Element 'GlassBarMainWindow' $process.Id
    Capture-Window $main (Join-Path $auditDirectory 'custom-effect-lab.png')
    $exportButton = Find-Element 'ExportEffect' $process.Id
    $exportButton.SetFocus()
    Start-Sleep -Milliseconds 300
    Capture-Window $main (Join-Path $auditDirectory 'custom-effect-lab-bottom.png')

    $magick = 'C:\Users\ethan\AppData\Local\Microsoft\WindowsApps\magick.exe'
    if (-not (Test-Path -LiteralPath $magick)) { throw 'ImageMagick is required to build the animated preview.' }
    $frames = @(
        Get-ChildItem -LiteralPath $frameDirectory -Filter 'aurora-*.png' | Sort-Object Name
        Get-ChildItem -LiteralPath $frameDirectory -Filter 'rain-*.png' | Sort-Object Name
    )
    $arguments = @('-delay', '9') + @($frames.FullName) + @('-loop', '0', '-layers', 'Optimize', $outputPath)
    & $magick @arguments
    if ($LASTEXITCODE -ne 0) { throw "ImageMagick exited with code $LASTEXITCODE." }

    Get-Item -LiteralPath $outputPath | Select-Object FullName, Length
}
finally {
    Get-Process GlassBar -ErrorAction SilentlyContinue | Where-Object Path -EQ $executable | Stop-Process -Force
    Start-Sleep -Milliseconds 700
    if ($hadSettings) { [IO.File]::WriteAllText($settingsPath, $settingsBackup) }
    elseif (Test-Path -LiteralPath $settingsPath) { Remove-Item -LiteralPath $settingsPath -Force }
    if ($wasInstalledRunning -and (Test-Path -LiteralPath $installedExecutable)) {
        Start-Process -FilePath $installedExecutable | Out-Null
    }
}
