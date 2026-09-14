using System.Windows.Media;

namespace GlassBar.Models;

public sealed class LaunchableApp
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public string Subtitle { get; init; } = "App";
    public string Kind { get; init; } = "App";
    public string SearchTerms { get; init; } = "";
    public ImageSource? Icon { get; init; }
    public string IconState => Icon is null ? "Fallback icon" : "Windows icon";
    public bool IsSystemApp { get; init; }
}
