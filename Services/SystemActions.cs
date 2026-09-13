using System.Diagnostics;
using GlassBar.Interop;

namespace GlassBar.Services;

public static class SystemActions
{
    public static void StartMenu() => TapHotkey(NativeMethods.VK_LWIN);
    public static void Search() => TapHotkey(NativeMethods.VK_LWIN, NativeMethods.VK_S);
    public static void TaskView() => TapHotkey(NativeMethods.VK_LWIN, NativeMethods.VK_TAB);
    public static void QuickSettings() => TapHotkey(NativeMethods.VK_LWIN, NativeMethods.VK_A);
    public static void Notifications() => TapHotkey(NativeMethods.VK_LWIN, NativeMethods.VK_N);
    public static void LockComputer() => NativeMethods.LockWorkStation();

    public static void OpenExplorer() => Start("explorer.exe");

    public static void OpenTerminal()
    {
        try { Start("wt.exe"); }
        catch { Start("powershell.exe"); }
    }

    public static void Start(string fileName)
    {
        Process.Start(new ProcessStartInfo(fileName) { UseShellExecute = true });
    }

    private static void TapHotkey(params byte[] keys)
    {
        foreach (var key in keys) NativeMethods.keybd_event(key, 0, 0, 0);
        for (var i = keys.Length - 1; i >= 0; i--) NativeMethods.keybd_event(keys[i], 0, NativeMethods.KEYEVENTF_KEYUP, 0);
    }
}
