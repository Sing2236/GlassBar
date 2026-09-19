using GlassBar.Interop;
using GlassBar.Services;

var monitor = Rect(0, 0, 1920, 1080);
var fullscreen = Rect(0, 0, 1920, 1080);
var windowed = Rect(100, 100, 1500, 900);

AssertFalse(
    FullscreenWindowDetector.IsFullscreenCandidate("msedge", "Chrome_WidgetWin_1", 0, false, fullscreen, monitor),
    "Fullscreen Edge video must not hide GlassBar.");
AssertFalse(
    FullscreenWindowDetector.IsFullscreenCandidate("chrome.exe", "Chrome_WidgetWin_1", 0, false, fullscreen, monitor),
    "Fullscreen Chrome video must not hide GlassBar.");
AssertFalse(
    FullscreenWindowDetector.IsFullscreenCandidate("vlc", "QtWindow", 0, false, fullscreen, monitor),
    "Fullscreen media players must not hide GlassBar.");
AssertFalse(
    FullscreenWindowDetector.IsFullscreenCandidate("notepad", "Notepad", NativeMethods.WS_CAPTION, true, fullscreen, monitor),
    "An ordinary maximized desktop window must not hide GlassBar.");
AssertTrue(
    FullscreenWindowDetector.IsFullscreenCandidate("Client-Win64-Shipping", "UnrealWindow", NativeMethods.WS_CAPTION,
        false, fullscreen, monitor),
    "A borderless game that retains caption bits, such as Wuthering Waves, must hide GlassBar.");
AssertTrue(
    FullscreenWindowDetector.IsFullscreenCandidate("game", "GameWindow", NativeMethods.WS_POPUP, false, fullscreen,
        monitor),
    "A popup-style fullscreen game must hide GlassBar.");
AssertFalse(
    FullscreenWindowDetector.IsFullscreenCandidate("game", "GameWindow", NativeMethods.WS_POPUP, false, windowed,
        monitor),
    "A windowed game must not hide GlassBar.");

Console.WriteLine("Fullscreen classification regression tests passed.");
return;

static NativeMethods.NativeRect Rect(int left, int top, int right, int bottom) => new()
{
    Left = left,
    Top = top,
    Right = right,
    Bottom = bottom
};

static void AssertTrue(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
}

static void AssertFalse(bool value, string message) => AssertTrue(!value, message);
