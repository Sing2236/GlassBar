using System.Windows.Media;

namespace GlassBar.Models;

public sealed class SearchPaletteItem
{
    public required string Name { get; init; }
    public required string Subtitle { get; init; }
    public required string Kind { get; init; }
    public ImageSource? Icon { get; init; }
    public AppItem? Window { get; init; }
    public LaunchableApp? Launchable { get; init; }
    public string? Url { get; init; }
}
