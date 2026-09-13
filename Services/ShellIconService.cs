using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassBar.Interop;

namespace GlassBar.Services;

public static class ShellIconService
{
    public static ImageSource? GetIcon(string path)
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
