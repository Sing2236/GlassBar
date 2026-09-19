using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using GlassBar.Interop;

namespace GlassBar.Services;

internal static class FullscreenWindowDetector
{
    private const int EdgeTolerance = 10;
    private static readonly HashSet<string> BrowserAndMediaProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "arc", "brave", "chrome", "firefox", "librewolf", "msedge", "msedgewebview2", "opera", "opera_gx",
        "vivaldi", "waterfox", "microsoft.media.player", "mpc-be", "mpc-be64", "mpc-hc", "mpc-hc64", "mpv",
        "plex", "plexamp", "potplayermini", "potplayermini64", "video.ui", "vlc", "wmplayer"
    };

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

        var style = NativeMethods.GetWindowLongPtr(foreground, NativeMethods.GWL_STYLE).ToInt64();
        return IsFullscreenCandidate(GetProcessName(processId), GetClassName(foreground), style,
            NativeMethods.IsZoomed(foreground), windowRect, monitorInfo.Monitor);
    }

    internal static bool IsFullscreenCandidate(string? processName, string? className, long style, bool isZoomed,
        NativeMethods.NativeRect window, NativeMethods.NativeRect monitor)
    {
        if (!EdgesMatch(window, monitor) || IsBrowserOrMediaSurface(processName, className)) return false;

        // Ordinary maximized desktop windows cover the monitor after GlassBar
        // hides the native taskbar. Borderless games can retain caption style
        // bits without being in Windows' maximized state, so only reject the
        // combination of a caption and a real maximized window.
        var hasCaption = (style & NativeMethods.WS_CAPTION) == NativeMethods.WS_CAPTION;
        return !hasCaption || !isZoomed;
    }

    private static bool IsBrowserOrMediaSurface(string? processName, string? className)
    {
        var normalizedProcess = Path.GetFileNameWithoutExtension(processName ?? string.Empty);
        if (BrowserAndMediaProcesses.Contains(normalizedProcess)) return true;
        return className is "Chrome_WidgetWin_1" or "MozillaWindowClass";
    }

    private static string GetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch { return string.Empty; }
    }

    private static string GetClassName(nint window)
    {
        var className = new StringBuilder(128);
        NativeMethods.GetClassName(window, className, className.Capacity);
        return className.ToString();
    }

    private static bool IsShellSurface(nint window)
    {
        return GetClassName(window) is "Progman" or "WorkerW" or "Shell_TrayWnd";
    }

    private static bool EdgesMatch(NativeMethods.NativeRect window, NativeMethods.NativeRect monitor) =>
        window.Left <= monitor.Left + EdgeTolerance &&
        window.Top <= monitor.Top + EdgeTolerance &&
        window.Right >= monitor.Right - EdgeTolerance &&
        window.Bottom >= monitor.Bottom - EdgeTolerance;
}
