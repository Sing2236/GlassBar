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
        var processes = new List<(int Id, string Name, long Memory, string? Path)>();

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

                    processes.Add((process.Id, process.ProcessName, process.WorkingSet64, TryGetProcessPath(process.Id)));
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
                Path = group.Select(item => item.Path).FirstOrDefault(path => !string.IsNullOrWhiteSpace(path)),
                ProcessIds = group.Select(item => item.Id).ToArray()
            })
            .OrderByDescending(item => item.Memory)
            .Select(item => new BackgroundProcessItem
            {
                Name = FormatProcessName(item.Name),
                Detail = $"{(item.Count > 1 ? $"{item.Count} processes · " : string.Empty)}{FormatMemory(item.Memory)}",
                Icon = string.IsNullOrWhiteSpace(item.Path) ? null : ShellIconService.GetIcon(item.Path),
                ProcessIds = item.ProcessIds
            })
            .ToList();
    }

    public IReadOnlyList<AppItem> GetOpenWindows(bool includePreviews = false)
    {
        var windows = new List<AppItem>();
        var shell = NativeMethods.GetShellWindow();
        var foreground = NativeMethods.GetForegroundWindow();
        var ownPid = Environment.ProcessId;

        NativeMethods.EnumWindows((handle, _) =>
        {
            if (handle == shell || !NativeMethods.IsWindowVisible(handle)) return true;
            if ((NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64() & NativeMethods.WS_EX_TOOLWINDOW) != 0) return true;
            if (NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;

            var length = NativeMethods.GetWindowTextLength(handle);
            if (length == 0) return true;
            var titleBuffer = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(handle, titleBuffer, titleBuffer.Capacity);

            NativeMethods.GetWindowThreadProcessId(handle, out var pidValue);
            if (pidValue == ownPid) return true;

            try
            {
                using var process = Process.GetProcessById((int)pidValue);
                var executablePath = TryGetProcessPath(process.Id);
                windows.Add(new AppItem
                {
                    Handle = handle,
                    Title = titleBuffer.ToString(),
                    ProcessName = process.ProcessName,
                    ExecutablePath = executablePath,
                    Icon = ExtractIcon(handle, process),
                    Preview = includePreviews ? CaptureWindowPreview(handle) : null,
                    IsActive = handle == foreground
                });
            }
            catch { }
            return true;
        }, nint.Zero);

        return windows;
    }

    public void Activate(AppItem item)
    {
        if (NativeMethods.IsIconic(item.Handle)) NativeMethods.ShowWindow(item.Handle, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(item.Handle);
    }

    public bool CloseWindow(AppItem item) =>
        item.Handle != nint.Zero && NativeMethods.PostMessage(item.Handle, NativeMethods.WM_CLOSE, nint.Zero, nint.Zero);

    public int CloseBackgroundProcesses(BackgroundProcessItem item)
    {
        var closed = 0;
        foreach (var processId in item.ProcessIds)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.Id == Environment.ProcessId || process.HasExited) continue;
                process.Kill(entireProcessTree: false);
                closed++;
            }
            catch { }
        }
        return closed;
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

    private static ImageSource? CaptureWindowPreview(nint window)
    {
        if (!NativeMethods.GetWindowRect(window, out var bounds)) return null;
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width < 2 || height < 2 || width > 8192 || height > 8192) return null;

        var screenDc = NativeMethods.GetDC(nint.Zero);
        if (screenDc == nint.Zero) return null;
        var memoryDc = nint.Zero;
        var bitmap = nint.Zero;
        var previous = nint.Zero;
        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            if (memoryDc == nint.Zero) return null;
            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
            if (bitmap == nint.Zero) return null;
            previous = NativeMethods.SelectObject(memoryDc, bitmap);

            var rendered = NativeMethods.PrintWindow(window, memoryDc, NativeMethods.PW_RENDERFULLCONTENT);
            if (!rendered)
            {
                var windowDc = NativeMethods.GetWindowDC(window);
                if (windowDc != nint.Zero)
                {
                    try
                    {
                        rendered = NativeMethods.BitBlt(memoryDc, 0, 0, width, height, windowDc, 0, 0,
                            NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
                    }
                    finally { NativeMethods.ReleaseDC(window, windowDc); }
                }
            }
            if (!rendered) return null;

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, nint.Zero, System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            const int targetWidth = 144;
            const int targetHeight = 224;
            var scale = Math.Max(targetWidth / (double)source.PixelWidth, targetHeight / (double)source.PixelHeight);
            var drawWidth = source.PixelWidth * scale;
            var drawHeight = source.PixelHeight * scale;
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
                context.DrawImage(source, new System.Windows.Rect(
                    (targetWidth - drawWidth) / 2,
                    (targetHeight - drawHeight) / 2,
                    drawWidth,
                    drawHeight));

            var preview = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
            preview.Render(visual);
            preview.Freeze();
            return IsBlankPreview(preview) ? null : preview;
        }
        catch { return null; }
        finally
        {
            if (previous != nint.Zero && memoryDc != nint.Zero) NativeMethods.SelectObject(memoryDc, previous);
            if (bitmap != nint.Zero) NativeMethods.DeleteObject(bitmap);
            if (memoryDc != nint.Zero) NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(nint.Zero, screenDc);
        }
    }

    private static bool IsBlankPreview(BitmapSource preview)
    {
        var stride = preview.PixelWidth * 4;
        var pixels = new byte[stride * preview.PixelHeight];
        preview.CopyPixels(pixels, stride, 0);
        var minimum = 255;
        var maximum = 0;
        var visibleSamples = 0;

        for (var y = 0; y < preview.PixelHeight; y += 8)
        for (var x = 0; x < preview.PixelWidth; x += 8)
        {
            var offset = y * stride + x * 4;
            var brightness = Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2]));
            minimum = Math.Min(minimum, brightness);
            maximum = Math.Max(maximum, brightness);
            if (brightness > 14) visibleSamples++;
        }

        return visibleSamples < 2 || maximum - minimum < 4;
    }
}
