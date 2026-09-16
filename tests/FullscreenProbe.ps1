param(
    [ValidateSet('Maximized', 'Fullscreen')]
    [string]$Mode = 'Fullscreen'
)

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FullscreenProbeNative {
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);
}
'@

$form = New-Object System.Windows.Forms.Form
$form.Text = "GlassBar $Mode Probe"
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$form.Bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$form.BackColor = [System.Drawing.Color]::Black
$form.ShowInTaskbar = $true
$form.TopMost = $true

if ($Mode -eq 'Fullscreen') {
    $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
} else {
    $form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::Sizable
    $form.WindowState = [System.Windows.Forms.FormWindowState]::Maximized
}

$form.Add_Shown({
    $form.Activate()
    [FullscreenProbeNative]::SetForegroundWindow($form.Handle) | Out-Null
})
[System.Windows.Forms.Application]::Run($form)
