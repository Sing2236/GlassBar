$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName Microsoft.VisualBasic

$projectRoot = Split-Path $PSScriptRoot -Parent
$executable = Join-Path $projectRoot 'bin\Release\net8.0-windows\GlassBar.exe'
$installedExecutable = Join-Path $env:LOCALAPPDATA 'Programs\GlassBar\GlassBar.exe'
$probeScript = Join-Path $projectRoot 'tests\FullscreenProbe.ps1'
$process = $null
$probe = $null

function Find-GlassBar([int]$processId, [int]$timeoutMilliseconds = 0) {
    $deadline = [DateTime]::UtcNow.AddMilliseconds($timeoutMilliseconds)
    $idCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'GlassBarMainWindow')
    $processCondition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $processId)
    $condition = New-Object System.Windows.Automation.AndCondition($idCondition, $processCondition)

    do {
        $element = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children, $condition)
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    return $null
}

function Start-Probe([string]$mode) {
    Start-Process powershell.exe -ArgumentList @(
        '-NoProfile', '-NonInteractive', '-Sta', '-ExecutionPolicy', 'Bypass',
        '-File', $probeScript, '-Mode', $mode
    ) -PassThru
}

function Test-GlassBarVisible([int]$processId, [int]$timeoutMilliseconds = 0) {
    $element = Find-GlassBar -processId $processId -timeoutMilliseconds $timeoutMilliseconds
    return $null -ne $element -and -not $element.Current.IsOffscreen
}

try {
    $knownExecutables = @($executable, $installedExecutable)
    Get-Process GlassBar -ErrorAction SilentlyContinue |
        Where-Object { $knownExecutables -contains $_.Path } |
        Stop-Process -Force
    Start-Sleep -Seconds 1

    # Put a known, ordinary maximized window in the foreground first so the
    # test does not inherit whatever app happened to be active on the desktop.
    $probe = Start-Probe 'Maximized'
    Start-Sleep -Seconds 1
    [Microsoft.VisualBasic.Interaction]::AppActivate($probe.Id) | Out-Null

    $process = Start-Process -FilePath $executable -ArgumentList '--safe' -PassThru
    Start-Sleep -Milliseconds 600
    if ($process.HasExited) {
        throw "GlassBar exited before the full-screen test with code $($process.ExitCode)."
    }
    [Microsoft.VisualBasic.Interaction]::AppActivate($probe.Id) | Out-Null
    Start-Sleep -Milliseconds 500
    $initial = Test-GlassBarVisible -processId $process.Id -timeoutMilliseconds 8000
    if (-not $initial) {
        throw "GlassBar process $($process.Id) stayed running but its window did not appear before the full-screen test."
    }

    $visibleOverMaximizedWindow = $initial
    Stop-Process -Id $probe.Id -Force
    $probe = $null
    Start-Sleep -Seconds 1

    $probe = Start-Probe 'Fullscreen'
    Start-Sleep -Seconds 1
    [Microsoft.VisualBasic.Interaction]::AppActivate($probe.Id) | Out-Null
    Start-Sleep -Seconds 1
    $hiddenForFullscreen = -not (Test-GlassBarVisible -processId $process.Id)
    Stop-Process -Id $probe.Id -Force
    $probe = $null
    Start-Sleep -Milliseconds 500

    # Return focus to a known non-fullscreen window. Otherwise an unrelated
    # fullscreen game or video behind the probe can immediately keep GlassBar hidden.
    $probe = Start-Probe 'Maximized'
    Start-Sleep -Seconds 1
    [Microsoft.VisualBasic.Interaction]::AppActivate($probe.Id) | Out-Null
    Start-Sleep -Seconds 1

    $restoredAfterFullscreen = Test-GlassBarVisible -processId $process.Id -timeoutMilliseconds 5000
    $result = [pscustomobject]@{
        VisibleOverMaximizedWindow = $visibleOverMaximizedWindow
        HiddenForFullscreen = $hiddenForFullscreen
        RestoredAfterFullscreen = $restoredAfterFullscreen
        AppResponsive = $process.Responding
    }
    $result | Format-List

    if (-not ($visibleOverMaximizedWindow -and $hiddenForFullscreen -and
        $restoredAfterFullscreen -and $process.Responding)) {
        throw 'The full-screen visibility regression test failed.'
    }
}
finally {
    if ($probe -and -not $probe.HasExited) { Stop-Process -Id $probe.Id -Force }
    if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    Start-Sleep -Seconds 1
}
