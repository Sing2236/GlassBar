using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassBar.Interop;

namespace GlassBar.Services;

public static class ShellIconService
{
    /// <summary>
    /// Icons for a given file path do not change, but the shell lookup that
    /// produces one is not free, so results are memoized. The key is a file
    /// path, which is drawn from a small set, and the cache is capped: once
    /// it fills it is cleared wholesale rather than allowed to grow, so a
    /// pathological caller cannot leak memory here. Failed lookups are not
    /// remembered, so a transient shell failure still recovers on the next
    /// call exactly as it did before this cache existed.
    /// </summary>
    private const int CacheCapacity = 256;

    private static readonly object CacheGate = new();
    private static readonly Dictionary<string, ImageSource> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? GetIcon(string path)
    {
        lock (CacheGate)
        {
            if (Cache.TryGetValue(path, out var cached)) return cached;
        }

        var icon = LoadIcon(path);
        if (icon is null) return null;

        lock (CacheGate)
        {
            if (Cache.Count >= CacheCapacity) Cache.Clear();
            Cache[path] = icon;
        }

        return icon;
    }

    private static ImageSource? LoadIcon(string path)
    {
        try
        {
            var result = NativeMethods.SHGetFileInfo(path, 0, out var info,
                (uint)Marshal.SizeOf<NativeMethods.ShellFileInfo>(), NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);
            if (result == nint.Zero || info.Icon == nint.Zero) return null;
            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty,
                    BitmapSizeOptions.FromWidthAndHeight(32, 32));
                source.Freeze();
                return source;
            }
            finally { NativeMethods.DestroyIcon(info.Icon); }
        }
        catch { return null; }
    }
}
