using GlassBar.Interop;

namespace GlassBar.Services;

public static class NativeTaskbar
{
    private static bool _hiddenByUs;
    // Explorer-owned window handles are cached here. Resolving them with
    // FindWindow/FindWindowEx walks the top-level window tree, which is far too
    // expensive to repeat on every guard tick, so it is only redone when a
    // cached handle has died (Explorer restart) or the monitor set changed.
    private static nint _primaryTray;
    private static readonly List<nint> _secondaryTrays = [];
    private static int _resolvedMonitorCount = -1;

    public static bool IsHiddenByUs => _hiddenByUs;

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

    public static void EnsureHidden()
    {
        if (!_hiddenByUs) return;
        EnsureTrayHandles();
        // Cheap local check: while Explorer has left the taskbar hidden there is
        // nothing to re-apply, so skip the cross-process ShowWindow calls.
        if (!AnyTrayVisible()) return;
        SetVisibility(false);
    }

    private static bool AnyTrayVisible()
    {
        if (_primaryTray != nint.Zero && NativeMethods.IsWindowVisible(_primaryTray)) return true;
        foreach (var tray in _secondaryTrays)
            if (NativeMethods.IsWindowVisible(tray)) return true;
        return false;
    }

    private static bool CacheIsValid()
    {
        if (_primaryTray == nint.Zero || !NativeMethods.IsWindow(_primaryTray)) return false;
        if (NativeMethods.GetSystemMetrics(NativeMethods.SM_CMONITORS) != _resolvedMonitorCount) return false;
        foreach (var tray in _secondaryTrays)
            if (!NativeMethods.IsWindow(tray)) return false;
        return true;
    }

    private static void EnsureTrayHandles()
    {
        if (CacheIsValid()) return;
        _primaryTray = NativeMethods.FindWindow("Shell_TrayWnd", null);
        _secondaryTrays.Clear();
        nint current = nint.Zero;
        while ((current = NativeMethods.FindWindowEx(nint.Zero, current, "Shell_SecondaryTrayWnd", null)) != nint.Zero)
            _secondaryTrays.Add(current);
        _resolvedMonitorCount = NativeMethods.GetSystemMetrics(NativeMethods.SM_CMONITORS);
    }

    private static void SetVisibility(bool visible)
    {
        EnsureTrayHandles();
        var command = visible ? NativeMethods.SW_SHOW : NativeMethods.SW_HIDE;
        if (_primaryTray != nint.Zero) NativeMethods.ShowWindow(_primaryTray, command);
        foreach (var tray in _secondaryTrays) NativeMethods.ShowWindow(tray, command);
    }
}
