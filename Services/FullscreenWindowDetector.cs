using System.Runtime.InteropServices;
using System.Text;
using GlassBar.Interop;

namespace GlassBar.Services;

internal static class FullscreenWindowDetector
{
    private const int EdgeTolerance = 3;

    internal static bool IsForegroundFullscreen(nint glassBarHandle)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == nint.Zero || foreground == glassBarHandle ||
            foreground == NativeMethods.GetShellWindow() || !NativeMethods.IsWindowVisible(foreground) ||
            NativeMethods.IsIconic(foreground))
            return false;

        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        if (processId == Environment.ProcessId || IsShellSurface(foreground)) return false;

        var style = NativeMethods.GetWindowLongPtr(foreground, NativeMethods.GWL_STYLE).ToInt64();
        var isPopup = (style & NativeMethods.WS_POPUP) != 0;
        var hasCaption = (style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION;
        if (!isPopup && hasCaption) return false;

        if (!NativeMethods.GetWindowRect(foreground, out var windowRect)) return false;
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
        Math.Abs(window.Left - monitor.Left) <= EdgeTolerance &&
        Math.Abs(window.Top - monitor.Top) <= EdgeTolerance &&
        Math.Abs(window.Right - monitor.Right) <= EdgeTolerance &&
        Math.Abs(window.Bottom - monitor.Bottom) <= EdgeTolerance;
}
