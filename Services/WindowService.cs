using System.Diagnostics;
using System.Text;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassBar.Interop;
using GlassBar.Models;

namespace GlassBar.Services;

public sealed class WindowService
{
    public IReadOnlyList<BackgroundProcessItem> GetBackgroundProcesses()
    {
        var ownPid = Environment.ProcessId;
        var ownSession = Process.GetCurrentProcess().SessionId;
        var processes = new List<(string Name, long Memory, string? Path)>();

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == ownPid || process.SessionId != ownSession || process.MainWindowHandle != nint.Zero)
                        continue;
                    if (process.ProcessName is "Idle" or "System" or "Registry" or "Memory Compression")
                        continue;

                    processes.Add((process.ProcessName, process.WorkingSet64, TryGetProcessPath(process.Id)));
                }
                catch { }
            }
        }

        return processes
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Name = group.Key,
                Count = group.Count(),
                Memory = group.Sum(item => item.Memory),
                Path = group.Select(item => item.Path).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path))
            })
            .OrderByDescending(item => item.Memory)
            .Select(item => new BackgroundProcessItem
            {
                Name = FormatProcessName(item.Name),
                Detail = $"{(item.Count > 1 ? $"{item.Count} processes · " : string.Empty)}{FormatMemory(item.Memory)}",
                Icon = string.IsNullOrWhiteSpace(item.Path) ? null : ShellIconService.GetIcon(item.Path)
            })
            .ToList();
    }

    public IReadOnlyList<AppItem> GetOpenWindows()
    {
        var windows = new List<AppItem>();
        var shell = NativeMethods.GetShellWindow();
        var foreground = NativeMethods.GetForegroundWindow();
        var ownPid = Environment.ProcessId;

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (handle == shell || !NativeMethods.IsWindowVisible(handle)) return true;
            if ((NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64() & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;
            if (NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

            var length = NativeMethods.GetWindowTextLength(handle);
            if (length == 0) return true;
            var titleBuffer = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(handle, titleBuffer, titleBuffer.Capacity);

            NativeMethods.GetWindowThreadProcessId(handle, out var pidValue);
            if (pidValue == ownPid) return true;

            try
            {
                using var process = Process.GetProcessById((int)pidValue);
                windows.Add(new AppItem
                {
                    Handle = handle,
                    Title = titleBuffer.ToString(),
                    ProcessName = process.ProcessName,
                    Icon = ExtractIcon(handle, process),
                    IsActive = handle == foreground
                });
            }
            catch { }
            return true;
        }, nint.Zero);

        return windows.Take(10).ToList();
    }

    public void Activate(AppItem item)
    {
        if (NativeMethods.IsIconic(item.Handle)) NativeMethods.ShowWindow(item.Handle, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(item.Handle);
    }

    private static string FormatProcessName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Background process" : char.ToUpperInvariant(value[0]) + value[1..];

    private static string FormatMemory(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024d * 1024 * 1024):0.0} GB"
        : $"{Math.Max(1, bytes / (1024d * 1024)):0} MB";

    private static string? TryGetProcessPath(int processId)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)processId);
        if (handle == nint.Zero) return null;
        try
        {
            var capacity = 1024;
            var buffer = new StringBuilder(capacity);
            return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref capacity) ? buffer.ToString() : null;
        }
        finally { NativeMethods.CloseHandle(handle); }
    }

    private static ImageSource? ExtractIcon(nint window, Process process)
    {
        try
        {
            var sharedIcon = NativeMethods.SendMessage(window, NativeMethods.WM_GETICON, NativeMethods.ICON_SMALL2, nint.Zero);
            if (sharedIcon == nint.Zero) sharedIcon = NativeMethods.SendMessage(window, NativeMethods.WM_GETICON, NativeMethods.ICON_SMALL, nint.Zero);
            if (sharedIcon == nint.Zero) sharedIcon = NativeMethods.SendMessage(window, NativeMethods.WM_GETICON, NativeMethods.ICON_BIG, nint.Zero);
            if (sharedIcon == nint.Zero) sharedIcon = NativeMethods.GetClassLongPtr(window, NativeMethods.GCLP_HICONSM);
            if (sharedIcon == nint.Zero) sharedIcon = NativeMethods.GetClassLongPtr(window, NativeMethods.GCLP_HICON);
            if (sharedIcon == nint.Zero)
            {
                var executable = process.MainModule?.FileName;
                return string.IsNullOrWhiteSpace(executable) ? null : ShellIconService.GetIcon(executable);
            }

            var ownedIcon = NativeMethods.CopyIcon(sharedIcon);
            if (ownedIcon == nint.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(ownedIcon, System.Windows.Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(32, 32));
                source.Freeze();
                return source;
            }
            finally { NativeMethods.DestroyIcon(ownedIcon); }
        }
        catch { return null; }
    }
}
