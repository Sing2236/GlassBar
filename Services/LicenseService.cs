using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GlassBar.Models;

namespace GlassBar.Services;

internal sealed class LicenseService
{
    internal const string PurchaseUrl =
        "https://www.paypal.com/cgi-bin/webscr?cmd=_xclick&business=Ethanhuynh365%40gmail.com&item_name=GlassBar+Pro+Lifetime&amount=5.00&currency_code=USD&no_shipping=1";

    private static readonly string LicensePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GlassBar", "license.json");
    private LicensePayload? _license;

    internal LicenseService()
    {
        Load();
    }

    internal bool IsPro => _license is not null;
    internal string LicensedEmail => _license?.Email ?? string.Empty;

    internal bool TryActivate(string licenseKey, out string message)
    {
        if (!TryValidate(licenseKey, out var payload, out message)) return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LicensePath)!);
            var temporaryPath = LicensePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new StoredLicense
            {
                LicenseKey = licenseKey.Trim()
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, LicensePath, true);
            _license = payload;
            message = $"Pro unlocked for {payload.Email}.";
            return true;
        }
        catch
        {
            message = "The key is valid, but GlassBar could not save it.";
            return false;
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(LicensePath)) return;
            var stored = JsonSerializer.Deserialize<StoredLicense>(File.ReadAllText(LicensePath));
            if (stored is not null && TryValidate(stored.LicenseKey, out var payload, out _))
                _license = payload;
        }
        catch
        {
            _license = null;
        }
    }

    private static bool TryValidate(string licenseKey, out LicensePayload payload, out string message)
    {
        payload = new LicensePayload();
        var parts = licenseKey.Trim().Split('.');
        if (parts.Length != 3 || parts[0] != "GB1")
        {
            message = "That does not look like a GlassBar license key.";
            return false;
        }

        try
        {
            var payloadBytes = FromBase64Url(parts[1]);
            var signature = FromBase64Url(parts[2]);
            using var rsa = RSA.Create();
            rsa.ImportFromPem(ReadPublicKey());
            if (!rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            {
                message = "That license signature is not valid.";
                return false;
            }

            payload = JsonSerializer.Deserialize<LicensePayload>(payloadBytes,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new LicensePayload();
            if (payload.Product != "GlassBar" || payload.Plan != "ProLifetime" ||
                string.IsNullOrWhiteSpace(payload.LicenseId) || !payload.Email.Contains('@') ||
                payload.IssuedAtUtc > DateTimeOffset.UtcNow.AddMinutes(10))
            {
                message = "That key is not a valid GlassBar Pro lifetime license.";
                return false;
            }

            message = string.Empty;
            return true;
        }
        catch
        {
            message = "GlassBar could not read that license key.";
            return false;
        }
    }

    private static string ReadPublicKey()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GlassBar.LicensePublicKey")
            ?? throw new InvalidOperationException("The GlassBar license verifier is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => string.Empty };
        return Convert.FromBase64String(padded);
    }

    private sealed class StoredLicense
    {
        public string LicenseKey { get; set; } = string.Empty;
    }
}
