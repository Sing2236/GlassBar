namespace GlassBar.Models;

internal sealed class UpdateManifest
{
    public string Version { get; set; } = string.Empty;
    public string InstallerUrl { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;

    // What the update prompt shows. Deliberately NOT part of the signed payload
    // built by UpdateManifestSignatureCore.CreatePayload: every GlassBar already
    // in the field verifies that exact byte string, so signing the notes as well
    // would make each existing install reject the next manifest and strand it.
    // The cost is that a compromised release could rewrite this text - but not the
    // installer, which is still pinned by the signed checksum.
    public string Notes { get; set; } = string.Empty;
}
