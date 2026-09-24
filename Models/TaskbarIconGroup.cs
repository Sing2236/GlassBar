using System.Windows.Media;

namespace GlassBar.Models;

/// <summary>
/// One taskbar icon slot. When "Group Stacked Icons" is off, or the app only
/// has one open window, this always wraps exactly one AppItem and behaves
/// identically to how running apps rendered before this existed. When the
/// setting is on and the same process has multiple open windows (e.g. three
/// Chrome windows), one TaskbarIconGroup wraps all of them, and the icon
/// renders as a "stack" that fans out into individual window icons on click
/// -- shown in a flyout panel (StackFanPanel in MainWindow.xaml), positioned
/// above/below or left/right of the bar to match its orientation, the same
/// way the existing Settings/Background-processes flyouts already work.
/// </summary>
public sealed class TaskbarIconGroup
{
    public required string ProcessName { get; init; }
    public required ImageSource? Icon { get; init; }
    public required IReadOnlyList<AppItem> Windows { get; init; }

    public bool IsStacked => Windows.Count > 1;
    public AppItem Primary => Windows[0];
    public bool IsActive => Windows.Any(window => window.IsActive);
    public int NotificationCount => Windows.Sum(window => window.NotificationCount);
}
