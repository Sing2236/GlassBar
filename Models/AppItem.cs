using System.ComponentModel;
using System.Windows.Media;

namespace GlassBar.Models;

public sealed class AppItem : INotifyPropertyChanged
{
    private ImageSource? _preview;

    public required nint Handle { get; init; }
    public required string Title { get; init; }
    public required string ProcessName { get; init; }
    public string? ExecutablePath { get; init; }
    public ImageSource? Icon { get; init; }

    /// <summary>
    /// On-demand window screenshot. Settable and observable because the
    /// Alt-Tab switcher now opens immediately with icons and fills these in
    /// from a background capture loop; a slow PrintWindow must not gate the
    /// first visible frame.
    /// </summary>
    public ImageSource? Preview
    {
        get => _preview;
        set
        {
            if (ReferenceEquals(_preview, value)) return;
            _preview = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
        }
    }

    public bool IsActive { get; init; }

    /// <summary>
    /// Best-effort count of current toast notifications attributed to this
    /// app (see NotificationBadgeService). Mutable and set after
    /// construction since it comes from a separate, independently-updating
    /// source than the window enumeration that builds the rest of this
    /// object. 0 means "no badge shown."
    /// </summary>
    public int NotificationCount { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
