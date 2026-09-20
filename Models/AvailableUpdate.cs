namespace GlassBar.Models;

internal sealed record AvailableUpdate(Version Version, Uri InstallerUri, string Sha256);
