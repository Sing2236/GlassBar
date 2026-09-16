using System.Windows.Media;

namespace GlassBar.Models;

public sealed class BackgroundProcessItem
{
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public ImageSource? Icon { get; init; }
}
