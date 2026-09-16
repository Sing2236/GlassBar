using System.Runtime.InteropServices;
using System.Text;

namespace GlassBar.Interop;

internal static class NativeMethods
{
    internal const int SW_HIDE = 0;
    internal const int SW_SHOW = 5;
    internal const int SW_RESTORE = 9;
    internal const int GWL_EXSTYLE = -20;
    internal const long WS_EX_TOOLWINDOW = 0x00000080L;
    internal const uint DWMWA_CLOAKED = 14;
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const int WM_HOTKEY = 0x0312;
    internal const int WM_GETICON = 0x007F;
    internal const int ICON_SMALL2 = 2;
    internal const int ICON_SMALL = 0;
    internal const int ICON_BIG = 1;
    internal const int GCLP_HICON = -14;
    internal const int GCLP_HICONSM = -34;
    internal const byte VK_LWIN = 0x5B;
    internal const byte VK_TAB = 0x09;
    internal const byte VK_S = 0x53;
    internal const byte VK_A = 0x41;
    internal const byte VK_N = 0x4E;
    internal const uint KEYEVENTF_KEYUP = 0x0002;
    internal const uint SHGFI_ICON = 0x000000100;
    internal const uint SHGFI_SMALLICON = 0x000000001;
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    internal delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint hWnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    internal static extern int GetWindowTextLength(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern bool QueryFullProcessImageName(nint process, int flags, StringBuilder executableName, ref int size);

    [DllImport("kernel32.dll")]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("user32.dll")]
    internal static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(nint hWnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(nint hWnd, int command);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(nint hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint FindWindowEx(nint parent, nint childAfter, string? className, string? windowName);

    [DllImport("user32.dll")]
    internal static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(nint hWnd, int id);

    [DllImport("user32.dll")]
    internal static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nuint extraInfo);

    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(nint handle);

    [DllImport("user32.dll")]
    internal static extern bool LockWorkStation();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SendMessage(nint hWnd, int message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    internal static extern nint GetClassLongPtr(nint hWnd, int index);

    [DllImport("user32.dll")]
    internal static extern nint CopyIcon(nint icon);

    [DllImport("user32.dll")]
    internal static extern int SetWindowCompositionAttribute(nint hwnd, ref WindowCompositionAttributeData data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern nint SHGetFileInfo(string path, uint fileAttributes, out ShellFileInfo fileInfo,
        uint fileInfoSize, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttributeData
    {
        public int Attribute;
        public nint Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ShellFileInfo
    {
        public nint Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
    }
}
