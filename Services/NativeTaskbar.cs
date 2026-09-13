using GlassBar.Interop;

namespace GlassBar.Services;

public static class NativeTaskbar
{
    private static bool _hiddenByUs;

    public static void Hide()
    {
        SetVisibility(false);
        _hiddenByUs = true;
    }

    public static void Show()
    {
        if (!_hiddenByUs) return;
        SetVisibility(true);
        _hiddenByUs = false;
    }

    public static void ForceShow()
    {
        SetVisibility(true);
        _hiddenByUs = false;
    }

    private static void SetVisibility(bool visible)
    {
        var command = visible ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE;
        var primary = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (primary != nint.Zero) NativeMethods.ShowWindow(primary, command);

        nint current = nint.Zero;
        while ((current = NativeMethods.FindWindowEx(nint.Zero, current, "Shell_SecondaryTrayWnd", null)) != nint.Zero)
            NativeMethods.ShowWindow(current, command);
    }
}
