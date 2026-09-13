using System.Windows.Media;

namespace GlassBar.Models;

public sealed class AppItem
{
    public required nint Handle { get; init; }
    public required string Title { get; init; }
    public required string ProcessName { get; init; }
    public ImageSource? Icon { get; init; }
    public bool IsActive { get; init; }
}
