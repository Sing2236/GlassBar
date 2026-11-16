using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassBar.Interop;
using GlassBar.Models;

namespace GlassBar.Services;

public sealed class WindowService
{
    /// <summary>
    /// Per-window values that are effectively constant for the lifetime of a
    /// window handle: the owning process identity and the icon. Only Title
    /// and IsActive change from tick to tick, so re-deriving these every
    /// second (a process open, up to three blocking cross-process
    /// WM_GETICON sends, and a fresh BitmapSource) was pure waste.
    /// </summary>
    /// <param name="ProcessId">
    /// The process that owned the handle when the entry was created. Window
    /// handles are recycled by the OS, so a cached entry is only trusted when
    /// the handle still maps to the same process and window class.
    /// </param>
    /// <param name="ClassName">
    /// The window class name, used together with ProcessId to detect a
    /// recycled handle without any cross-process call.
    /// </param>
    private sealed record WindowInfo(uint ProcessId, string ClassName, string ProcessName, string? ExecutablePath, ImageSource? Icon);

    /// <summary>
    /// Guards the cache below. Every caller so far is the WPF UI thread, but
    /// the method is public, so keep concurrent enumerations safe.
    /// </summary>
    private readonly object _cacheGate = new();

    /// <summary>
    /// Keyed on the window handle, which is drawn from a small system-owned
    /// set. Bounded by pruning every handle that was not seen in the current
    /// enumeration (see GetOpenWindows), so it cannot grow as windows open
    /// and close.
    /// </summary>
    private readonly Dictionary<nint, WindowInfo> _windowCache = [];

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
        // Handles that survived the current enumeration. Used to drop cache
        // entries for windows that have gone away.
        var seen = new HashSet<nint>();
        var classBuffer = new StringBuilder(256);

        lock (_cacheGate)
        {
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

                seen.Add(handle);

                // Reused across windows so the identity check stays
                // allocation-free on the common (cache hit) path.
                classBuffer.Clear();
                NativeMethods.GetClassName(handle, classBuffer, classBuffer.Capacity);
                var className = classBuffer.ToString();

                if (!_windowCache.TryGetValue(handle, out var info) ||
                    info.ProcessId != pidValue ||
                    !string.Equals(info.ClassName, className, StringComparison.Ordinal))
                {
                    try
                    {
                        using var process = Process.GetProcessById((int)pidValue);
                        if (process.ProcessName.Equals("GlassBar", StringComparison.OrdinalIgnoreCase)) return true;
                        info = new WindowInfo(pidValue, className, process.ProcessName,
                            TryGetProcessPath(process.Id), ExtractIcon(handle, process));
                        _windowCache[handle] = info;
                    }
                    catch { return true; }
                }

                windows.Add(new AppItem
                {
                    Handle = handle,
                    Title = titleBuffer.ToString(),
                    ProcessName = info.ProcessName,
                    ExecutablePath = info.ExecutablePath,
                    Icon = info.Icon,
                    Preview = includePreviews ? CaptureWindowPreview(handle) : null,
                    IsActive = handle == foreground
                });
                return true;
            }, nint.Zero);

            PruneWindowCache(seen);
        }

        return windows;
    }

    /// <summary>
    /// Drops cached window info for handles that were not part of the
    /// enumeration just completed, so closed windows cannot accumulate.
    /// </summary>
    private void PruneWindowCache(HashSet<nint> seen)
    {
        if (_windowCache.Count == 0 || seen.Count == _windowCache.Count) return;

        List<nint>? stale = null;
        foreach (var handle in _windowCache.Keys)
            if (!seen.Contains(handle)) (stale ??= []).Add(handle);

        if (stale is null) return;
        foreach (var handle in stale) _windowCache.Remove(handle);
    }

    public bool Activate(AppItem item)
    {
        if (item.Handle == nint.Zero) return false;

        var currentThread = NativeMethods.GetCurrentThreadId();
        var foregroundThread = NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), nint.Zero);
        var targetThread = NativeMethods.GetWindowThreadProcessId(item.Handle, nint.Zero);
        var attachedThreads = new List<uint>(2);

        try
        {
            foreach (var thread in new[] { foregroundThread, targetThread }.Distinct())
            {
                if (thread != 0 && thread != currentThread &&
                    NativeMethods.AttachThreadInput(currentThread, thread, true))
                    attachedThreads.Add(thread);
            }

            if (NativeMethods.IsIconic(item.Handle)) NativeMethods.ShowWindow(item.Handle, NativeMethods.SW_RESTORE);
            NativeMethods.BringWindowToTop(item.Handle);
            return NativeMethods.SetForegroundWindow(item.Handle);
        }
        finally
        {
            for (var index = attachedThreads.Count - 1; index >= 0; index--)
                NativeMethods.AttachThreadInput(currentThread, attachedThreads[index], false);
        }
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
            var sharedIcon = QueryWindowIcon(window, NativeMethods.ICON_SMALL2);
            if (sharedIcon == nint.Zero) sharedIcon = QueryWindowIcon(window, NativeMethods.ICON_SMALL);
            if (sharedIcon == nint.Zero) sharedIcon = QueryWindowIcon(window, NativeMethods.ICON_BIG);
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

    /// <summary>
    /// WM_GETICON is a cross-process query. Plain SendMessage waits forever if
    /// that app's UI thread is stuck, so ask with a short timeout and treat a
    /// timeout as "no icon from this window" (the caller falls back to the
    /// executable icon).
    /// </summary>
    private static nint QueryWindowIcon(nint window, int iconType)
    {
        const uint timeoutMilliseconds = 100;
        var delivered = NativeMethods.SendMessageTimeout(window, NativeMethods.WM_GETICON, iconType, nint.Zero,
            NativeMethods.SMTO_ABORTIFHUNG | NativeMethods.SMTO_BLOCK, timeoutMilliseconds, out var result);
        return delivered == nint.Zero ? nint.Zero : result;
    }

    /// <summary>
    /// On-demand single-window screenshot for hover previews (e.g. the
    /// fanned-out icons in a stacked group). Deliberately not called eagerly
    /// for every open window on every bar refresh -- PrintWindow/BitBlt
    /// capture has a real per-call cost, so this is meant to be invoked only
    /// when a preview is actually about to be shown (mouse-enter), not
    /// speculatively for windows nobody is looking at.
    /// </summary>
    public static ImageSource? CapturePreview(nint window) => CaptureWindowPreview(window);

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

            // PW_RENDERFULLCONTENT is the right first attempt for DWM-composed
            // windows, but some GPU-rendered apps answer it with an all-white
            // frame. Retrying with the classic flag, then with a screen copy,
            // recovers a real image for most of those instead of showing a
            // white card.
            foreach (var flags in new[] { NativeMethods.PW_RENDERFULLCONTENT, 0u })
            {
                if (!NativeMethods.PrintWindow(window, memoryDc, flags)) continue;
                if (ToPreviewSize(bitmap) is { } candidate) return candidate;
            }

            var windowDc = NativeMethods.GetWindowDC(window);
            if (windowDc != nint.Zero)
            {
                try
                {
                    if (NativeMethods.BitBlt(memoryDc, 0, 0, width, height, windowDc, 0, 0,
                            NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                        if (ToPreviewSize(bitmap) is { } candidate) return candidate;
                }
                finally { NativeMethods.ReleaseDC(window, windowDc); }
            }

            return null;
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

    /// <summary>
    /// Scales a raw window capture into the switcher's card size, returning
    /// null when the result carries no usable image (blank or uniformly
    /// white), which makes the caller try another capture method.
    /// </summary>
    private static ImageSource? ToPreviewSize(nint bitmap)
    {
        // Capture (PrintWindow/BitBlt) is deliberately callable off the UI
        // thread so the Alt-Tab switcher can stream previews in without
        // blocking, but the scaling below uses WPF visual objects. Hop to the
        // dispatcher for that part so it never matters which thread called.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return dispatcher.Invoke(() => ToPreviewSize(bitmap));

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

    private static bool IsBlankPreview(BitmapSource preview)
    {
        var stride = preview.PixelWidth * 4;
        var pixels = new byte[stride * preview.PixelHeight];
        preview.CopyPixels(pixels, stride, 0);
        var minimum = 255;
        var maximum = 0;
        var visibleSamples = 0;
        var nearWhiteSamples = 0;
        var totalSamples = 0;

        for (var y = 0; y < preview.PixelHeight; y += 8)
        for (var x = 0; x < preview.PixelWidth; x += 8)
        {
            var offset = y * stride + x * 4;
            var brightness = Math.Max(pixels[offset], Math.Max(pixels[offset + 1], pixels[offset + 2]));
            minimum = Math.Min(minimum, brightness);
            maximum = Math.Max(maximum, brightness);
            totalSamples++;
            if (brightness > 14) visibleSamples++;
            if (brightness >= 245) nearWhiteSamples++;
        }

        // A failed PrintWindow on a composited window comes back as a solid
        // white client area. Real light-themed windows still have text, edges
        // or a frame somewhere in the sample grid, so "every sampled pixel is
        // near-white" is the signature of a capture that told us nothing --
        // the caller then falls back to the app icon.
        var uniformlyWhite = minimum >= 245 && nearWhiteSamples >= totalSamples * 0.98;
        return visibleSamples < 2 || maximum - minimum < 4 || uniformlyWhite;
    }
}
