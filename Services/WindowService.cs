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
