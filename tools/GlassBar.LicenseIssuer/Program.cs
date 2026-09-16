using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GlassBar.Security;

if (args.Length == 0)
{
    ShowUsage();
    return 1;
}

try
{
    return args[0].ToLowerInvariant() switch
    {
        "init" => InitializeKeys(args),
        "issue" => IssueLicense(args),
        "sign-update" => SignUpdate(args),
        "verify-update" => VerifyUpdate(args),
        _ => ShowUsage()
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static int InitializeKeys(string[] arguments)
{
    if (arguments.Length != 3) return ShowUsage();
    var privatePath = Path.GetFullPath(arguments[1]);
    var publicPath = Path.GetFullPath(arguments[2]);
    if (File.Exists(privatePath)) throw new InvalidOperationException($"Refusing to overwrite {privatePath}");

    Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(publicPath)!);
    using var rsa = RSA.Create(3072);
    File.WriteAllText(privatePath, rsa.ExportPkcs8PrivateKeyPem());
    File.WriteAllText(publicPath, rsa.ExportSubjectPublicKeyInfoPem());
    Console.WriteLine($"Private key: {privatePath}");
    Console.WriteLine($"Public key:  {publicPath}");
    return 0;
}

static int IssueLicense(string[] arguments)
{
    if (arguments.Length != 3) return ShowUsage();
    var privatePath = Path.GetFullPath(arguments[1]);
    var email = arguments[2].Trim().ToLowerInvariant();
    if (!email.Contains('@') || email.Length > 254) throw new InvalidOperationException("Enter a valid buyer email address.");
    if (!File.Exists(privatePath)) throw new FileNotFoundException("The private license key was not found.", privatePath);

    using var rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(privatePath));
    var payload = new LicensePayload(
        "GlassBar",
        "ProLifetime",
        email,
        Guid.NewGuid().ToString("N"),
        DateTimeOffset.UtcNow);
    var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    var signature = rsa.SignData(payloadBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    Console.WriteLine($"GB1.{Base64Url(payloadBytes)}.{Base64Url(signature)}");
    return 0;
}

static int SignUpdate(string[] arguments)
{
    if (arguments.Length != 7) return ShowUsage();
    var privatePath = Path.GetFullPath(arguments[1]);
    var outputPath = Path.GetFullPath(arguments[2]);
    var version = arguments[3].Trim();
    var installerUrl = arguments[4].Trim();
    var sha256 = arguments[5].Trim().ToLowerInvariant();
    var expectedRepository = arguments[6].Trim();

    ValidateUpdateFields(version, installerUrl, sha256, expectedRepository);
    if (!File.Exists(privatePath)) throw new FileNotFoundException("The update signing key was not found.", privatePath);

    using var rsa = RSA.Create();
    rsa.ImportFromPem(File.ReadAllText(privatePath));
    var signature = rsa.SignData(UpdateManifestSignatureCore.CreatePayload(version, installerUrl, sha256),
        HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    var manifest = new UpdateManifest(version, installerUrl, sha256, Convert.ToBase64String(signature));
    Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
    File.WriteAllText(outputPath, JsonSerializer.Serialize(manifest,
        new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    Console.WriteLine(outputPath);
    return 0;
}

static int VerifyUpdate(string[] arguments)
{
    if (arguments.Length != 4) return ShowUsage();
    var publicPath = Path.GetFullPath(arguments[1]);
    var manifestPath = Path.GetFullPath(arguments[2]);
    var expectedRepository = arguments[3].Trim();
    if (!File.Exists(publicPath)) throw new FileNotFoundException("The update public key was not found.", publicPath);
    if (!File.Exists(manifestPath)) throw new FileNotFoundException("The update manifest was not found.", manifestPath);

    var manifest = JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(manifestPath),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException("The update manifest is invalid.");
    ValidateUpdateFields(manifest.Version, manifest.InstallerUrl, manifest.Sha256, expectedRepository);

    var verified = UpdateManifestSignatureCore.Verify(manifest.Version, manifest.InstallerUrl, manifest.Sha256,
        manifest.Signature, File.ReadAllText(publicPath));
    if (!verified) throw new InvalidOperationException("The update manifest signature is invalid.");
    Console.WriteLine("Update manifest signature verified.");
    return 0;
}

static void ValidateUpdateFields(string version, string installerUrl, string sha256, string expectedRepository)
{
    if (!Version.TryParse(version, out _)) throw new InvalidOperationException("The update version is invalid.");
    if (sha256.Length != 64 || sha256.Any(character => !Uri.IsHexDigit(character)))
        throw new InvalidOperationException("The installer SHA-256 is invalid.");
    if (!Uri.TryCreate(installerUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
        !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
        !uri.AbsolutePath.StartsWith($"/{expectedRepository.Trim('/')}/releases/download/", StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("The installer URL is outside the expected GitHub repository.");
}

static string Base64Url(byte[] value) => Convert.ToBase64String(value)
    .TrimEnd('=')
    .Replace('+', '-')
    .Replace('/', '_');

static int ShowUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  GlassBar.LicenseIssuer init <private.pem> <public.pem>");
    Console.Error.WriteLine("  GlassBar.LicenseIssuer issue <private.pem> <buyer-email>");
    Console.Error.WriteLine("  GlassBar.LicenseIssuer sign-update <private.pem> <output.json> <version> <installer-url> <sha256> <owner/repo>");
    Console.Error.WriteLine("  GlassBar.LicenseIssuer verify-update <public.pem> <manifest.json> <owner/repo>");
    return 1;
}

internal sealed record LicensePayload(
    string Product,
    string Plan,
    string Email,
    string LicenseId,
    DateTimeOffset IssuedAtUtc);

internal sealed record UpdateManifest(
    string Version,
    string InstallerUrl,
    string Sha256,
    string Signature);
