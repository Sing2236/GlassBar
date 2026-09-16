using System.Text.Json.Serialization;
using System.Windows.Media;

namespace GlassBar.Models;

public sealed class PinnedAppConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public required string DisplayName { get; set; }
    public required string Target { get; set; }

    [JsonIgnore]
    public ImageSource? Icon { get; set; }
}
