namespace GlassBar.Models;

/// <summary>
/// An update the prompt can offer. <paramref name="Notes"/> is the patch notes
/// shown in that prompt, already sanitised for display by the service.
/// </summary>
internal sealed record AvailableUpdate(Version Version, Uri InstallerUri, string Sha256, string Notes = "");
