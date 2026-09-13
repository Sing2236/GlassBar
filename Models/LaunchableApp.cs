using System.Windows.Media;

namespace GlassBar.Models;

public sealed class LaunchableApp
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public ImageSource? Icon { get; init; }
    public bool IsSystemApp { get; init; }
}
