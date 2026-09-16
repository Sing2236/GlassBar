using System.IO;
using System.Reflection;
using GlassBar.Models;
using GlassBar.Security;

namespace GlassBar.Services;

internal static class UpdateManifestSignature
{
    private const string ResourceName = "GlassBar.UpdatePublicKey";
    internal static bool Verify(UpdateManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.Version) ||
            string.IsNullOrWhiteSpace(manifest.InstallerUrl) ||
            string.IsNullOrWhiteSpace(manifest.Sha256) ||
            string.IsNullOrWhiteSpace(manifest.Signature))
            return false;

        return UpdateManifestSignatureCore.Verify(manifest.Version, manifest.InstallerUrl, manifest.Sha256,
            manifest.Signature, ReadPublicKey());
    }

    private static string ReadPublicKey()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The GlassBar update verifier is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
