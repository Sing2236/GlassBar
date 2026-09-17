using System.Runtime.InteropServices;
using System.Windows.Threading;
using GlassBar.Interop;
using GlassBar.Models;

namespace GlassBar.Services;

public sealed class AltTabService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Func<BarSettings> _settings;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardCallback;
    private nint _keyboardHook;
    private AltTabWindow? _switcher;
    private bool _sequenceActive;
    private bool _disposed;

    public AltTabService(Dispatcher dispatcher, Func<BarSettings> settings)
    {
        _dispatcher = dispatcher;
        _settings = settings;
        _keyboardCallback = KeyboardHook;
        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _keyboardCallback,
            NativeMethods.GetModuleHandle(null),
            0);
    }

    private nint KeyboardHook(int code, nint message, nint data)
    {
        if (code < 0 || _disposed)
            return NativeMethods.CallNextHookEx(_keyboardHook, code, message, data);

        var key = Marshal.PtrToStructure<NativeMethods.LowLevelKeyboardInput>(data);
        var keyDown = message == NativeMethods.WM_KEYDOWN || message == NativeMethods.WM_SYSKEYDOWN;
        var keyUp = message == NativeMethods.WM_KEYUP || message == NativeMethods.WM_SYSKEYUP;

        if (key.VirtualKey == NativeMethods.VK_TAB && keyDown)
        {
            var altDown = (key.Flags & NativeMethods.LLKHF_ALTDOWN) != 0 ||
                          (NativeMethods.GetAsyncKeyState(NativeMethods.VK_MENU) & 0x8000) != 0;
            if (!altDown || !_settings().AltTabEnabled)
                return NativeMethods.CallNextHookEx(_keyboardHook, code, message, data);

            _sequenceActive = true;
            var reverse = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_SHIFT) & 0x8000) != 0;
            _dispatcher.BeginInvoke(() => ShowOrCycle(reverse));
            return 1;
        }

        if (_sequenceActive && key.VirtualKey == NativeMethods.VK_TAB && keyUp)
            return 1;

        if (_sequenceActive && key.VirtualKey == NativeMethods.VK_ESCAPE && keyDown)
        {
            _sequenceActive = false;
            _dispatcher.BeginInvoke(Cancel);
            return 1;
        }

        if (_sequenceActive &&
            (key.VirtualKey == NativeMethods.VK_MENU || key.VirtualKey == NativeMethods.VK_LMENU || key.VirtualKey == NativeMethods.VK_RMENU) &&
            keyUp)
        {
            _sequenceActive = false;
            _dispatcher.BeginInvoke(Complete);
            return NativeMethods.CallNextHookEx(_keyboardHook, code, message, data);
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, code, message, data);
    }

    private void ShowOrCycle(bool reverse)
    {
        if (_switcher is { IsVisible: true })
        {
            _switcher.Roll(reverse);
            return;
        }

        _switcher ??= new AltTabWindow();
        if (!_switcher.Open(_settings(), reverse))
            _sequenceActive = false;
    }

    private void Complete()
    {
        if (_switcher is { IsVisible: true }) _switcher.CompleteSelection();
    }

    private void Cancel()
    {
        if (_switcher is { IsVisible: true }) _switcher.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sequenceActive = false;
        if (_keyboardHook != nint.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = nint.Zero;
        }
        _switcher?.Close();
    }
}
