using System.Windows.Input;
using GlassBar.Interop;

namespace GlassBar.Models;

public readonly record struct HotkeyGesture(uint Modifiers, uint VirtualKey, string DisplayText)
{
    public uint RegistrationModifiers => Modifiers | NativeMethods.MOD_NOREPEAT;

    public static bool TryCreate(Key key, ModifierKeys modifiers, out HotkeyGesture gesture, out string error)
    {
        gesture = default;
        error = string.Empty;

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.None)
        {
            error = "Press a letter, number, function key, or Space with the shortcut.";
            return false;
        }

        var nativeModifiers = ToNativeModifiers(modifiers);
        if ((nativeModifiers & (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_WIN)) == 0)
        {
            error = "Include Ctrl, Alt, or the Windows key so normal typing stays available.";
            return false;
        }

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey == 0)
        {
            error = "That key cannot be used as a global shortcut.";
            return false;
        }

        gesture = new HotkeyGesture(nativeModifiers, virtualKey, Format(nativeModifiers, key));
        return true;
    }

    public static bool TryParse(string? value, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var parts = value.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        var modifiers = ModifierKeys.None;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            switch (parts[index].ToLowerInvariant())
            {
                case "ctrl":
                case "control": modifiers |= ModifierKeys.Control; break;
                case "alt": modifiers |= ModifierKeys.Alt; break;
                case "shift": modifiers |= ModifierKeys.Shift; break;
                case "win":
                case "windows": modifiers |= ModifierKeys.Windows; break;
                default: return false;
            }
        }

        var keyText = parts[^1].Equals("Space", StringComparison.OrdinalIgnoreCase) ? nameof(Key.Space) : parts[^1];
        if (!Enum.TryParse<Key>(keyText, ignoreCase: true, out var key)) return false;
        return TryCreate(key, modifiers, out gesture, out _);
    }

    public static bool IsWindowsReserved(HotkeyGesture gesture)
    {
        var key = KeyInterop.KeyFromVirtualKey((int)gesture.VirtualKey);
        var modifiers = gesture.Modifiers;

        if (key is Key.Tab or Key.Escape && (modifiers & NativeMethods.MOD_ALT) != 0) return true;
        if (key == Key.Escape && (modifiers & NativeMethods.MOD_CONTROL) != 0) return true;
        if (key == Key.Delete && (modifiers & (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT)) ==
            (NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT)) return true;
        if (key == Key.F4 && (modifiers & NativeMethods.MOD_ALT) != 0) return true;

        if ((modifiers & NativeMethods.MOD_WIN) == 0) return false;
        return key is Key.L or Key.D or Key.E or Key.R or Key.I or Key.S or Key.X or Key.Tab;
    }

    private static uint ToNativeModifiers(ModifierKeys modifiers)
    {
        var result = 0u;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= NativeMethods.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= NativeMethods.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= NativeMethods.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= NativeMethods.MOD_WIN;
        return result;
    }

    private static string Format(uint modifiers, Key key)
    {
        var parts = new List<string>(5);
        if ((modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((modifiers & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(key == Key.Space ? "Space" : key.ToString());
        return string.Join(" + ", parts);
    }
}
