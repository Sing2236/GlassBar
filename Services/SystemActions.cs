using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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
    public static void ShutDownComputer() => RunShutdown("/s /t 0");
    public static void RestartComputer() => RunShutdown("/r /t 0");

    public static void SleepComputer()
    {
        if (!NativeMethods.SetSuspendState(hibernate: false, forceCritical: false, disableWakeEvent: false))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not put this computer to sleep.");
    }

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

    private static void RunShutdown(string arguments)
    {
        var shutdownPath = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
        Process.Start(new ProcessStartInfo(shutdownPath, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private static void TapHotkey(params byte[] keys)
    {
        foreach (var key in keys) NativeMethods.keybd_event(key, 0, 0, 0);
        for (var i = keys.Length - 1; i >= 0; i--) NativeMethods.keybd_event(keys[i], 0, NativeMethods.KEYEVENTF_KEYUP, 0);
    }
}
