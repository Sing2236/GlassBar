using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

static string Base64Url(byte[] value) => Convert.ToBase64String(value)
    .TrimEnd('=')
    .Replace('+', '-')
    .Replace('/', '_');

static int ShowUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  GlassBar.LicenseIssuer init <private.pem> <public.pem>");
    Console.Error.WriteLine("  GlassBar.LicenseIssuer issue <private.pem> <buyer-email>");
    return 1;
}

internal sealed record LicensePayload(
    string Product,
    string Plan,
    string Email,
    string LicenseId,
    DateTimeOffset IssuedAtUtc);
