using System.Runtime.InteropServices;
using System.Text;
using GlassBar.Interop;

namespace GlassBar.Services;

internal static class FullscreenWindowDetector
{
    private const int EdgeTolerance = 10;

    internal static bool IsForegroundFullscreen(nint glassBarHandle)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == nint.Zero || foreground == glassBarHandle ||
            foreground == NativeMethods.GetShellWindow() || !NativeMethods.IsWindowVisible(foreground) ||
            NativeMethods.IsIconic(foreground))
            return false;

        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (processId == Environment.ProcessId || IsShellSurface(foreground)) return false;

        if (NativeMethods.DwmGetWindowAttribute(foreground, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;
        if (NativeMethods.DwmGetWindowAttribute(foreground, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out NativeMethods.NativeRect windowRect, Marshal.SizeOf<NativeMethods.NativeRect>()) != 0 &&
            !NativeMethods.GetWindowRect(foreground, out windowRect)) return false;
        var monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == nint.Zero) return false;

        var monitorInfo = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo)) return false;

        return EdgesMatch(windowRect, monitorInfo.Monitor);
    }

    private static bool IsShellSurface(nint window)
    {
        var className = new StringBuilder(64);
        NativeMethods.GetClassName(window, className, className.Capacity);
        return className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd";
    }

    private static bool EdgesMatch(NativeMethods.NativeRect window, NativeMethods.NativeRect monitor) =>
        window.Left <= monitor.Left + EdgeTolerance &&
        window.Top <= monitor.Top + EdgeTolerance &&
        window.Right >= monitor.Right - EdgeTolerance &&
        window.Bottom >= monitor.Bottom - EdgeTolerance;
}
