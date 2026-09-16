namespace GlassBar.Models;

internal sealed class UpdateManifest
{
    public string Version { get; set; } = string.Empty;
    public string InstallerUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}
