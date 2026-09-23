using System.Windows.Media;

namespace GlassBar.Models;

public sealed class AppItem
{
    public required nint Handle { get; init; }
    public required string Title { get; init; }
    public required string ProcessName { get; init; }
    public string? ExecutablePath { get; init; }
    public ImageSource? Icon { get; init; }
    public ImageSource? Preview { get; init; }
    public bool IsActive { get; init; }

    /// <summary>
    /// Best-effort count of current toast notifications attributed to this
    /// app (see NotificationBadgeService). Mutable and set after
    /// construction since it comes from a separate, independently-updating
    /// source than the window enumeration that builds the rest of this
    /// object. 0 means "no badge shown."
    /// </summary>
    public int NotificationCount { get; set; }
}
