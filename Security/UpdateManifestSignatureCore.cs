using System.Security.Cryptography;
using System.Text;

namespace GlassBar.Security;

public static class UpdateManifestSignatureCore
{
    private const string DomainSeparator = "GlassBarUpdateV1";

    public static byte[] CreatePayload(string version, string installerUrl, string sha256) =>
        Encoding.UTF8.GetBytes(string.Join('\n', DomainSeparator, version.Trim(), installerUrl.Trim(),
            sha256.Trim().ToLowerInvariant()));

    public static bool Verify(string version, string installerUrl, string sha256, string signature, string publicKey)
    {
        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKey);
            return rsa.VerifyData(CreatePayload(version, installerUrl, sha256), Convert.FromBase64String(signature),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch
        {
            return false;
        }
    }
}
