using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GlassBar.Models;

namespace GlassBar.Services;

internal static class UpdateService
{
    private const string ManifestUrl =
        "https://github.com/Sing2236/GlassBar/releases/latest/download/update.json";
    private static readonly HttpClient Client = CreateClient();

    internal static async Task<bool> TryLaunchUpdateAsync()
    {
        try
        {
            var manifestJson = await Client.GetStringAsync(ManifestUrl);
            var manifest = JsonSerializer.Deserialize<UpdateManifest>(manifestJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (manifest is null || !UpdateManifestSignature.Verify(manifest) ||
                !Version.TryParse(manifest.Version, out var availableVersion)) return false;

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            if (availableVersion <= currentVersion || !TryGetInstallerUri(manifest.InstallerUrl, out var installerUri))
                return false;

            var installerPath = await DownloadAndVerifyAsync(installerUri, availableVersion, manifest.Sha256);
            if (installerPath is null) return false;

            return LaunchUpdateBootstrap(installerPath);
        }
        catch (Exception exception)
        {
            LogFailure(exception);
            return false;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GlassBar-AutoUpdater/1.0");
        return client;
    }

    private static bool TryGetInstallerUri(string value, out Uri installerUri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps &&
            parsed.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
            parsed.AbsolutePath.StartsWith("/Sing2236/GlassBar/releases/download/", StringComparison.OrdinalIgnoreCase))
        {
            installerUri = parsed;
            return true;
        }

        installerUri = null!;
        return false;
    }

    private static async Task<string?> DownloadAndVerifyAsync(Uri installerUri, Version version, string expectedSha256)
    {
        if (expectedSha256.Length != 64 || expectedSha256.Any(character => !Uri.IsHexDigit(character))) return null;

        var updateFolder = Path.Combine(Path.GetTempPath(), "GlassBar", "updates");
        Directory.CreateDirectory(updateFolder);
        var installerPath = Path.Combine(updateFolder, $"GlassBarSetup-{version}.exe");
        var partialPath = installerPath + ".partial";

        using (var response = await Client.GetAsync(installerUri, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = File.Create(partialPath);
            await source.CopyToAsync(destination);
        }

        await using (var installer = File.OpenRead(partialPath))
        {
            var actualSha256 = Convert.ToHexString(await SHA256.HashDataAsync(installer));
            if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partialPath);
                return null;
            }
        }

        File.Move(partialPath, installerPath, true);
        return installerPath;
    }

    private static bool LaunchUpdateBootstrap(string installerPath)
    {
        var installedExecutable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "GlassBar", "GlassBar.exe");
        var escapedInstaller = installerPath.Replace("'", "''");
        var escapedExecutable = installedExecutable.Replace("'", "''");
        var script = $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; " +
                     $"$setup = Start-Process -FilePath '{escapedInstaller}' " +
                     "-ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CLOSEAPPLICATIONS' " +
                     "-WindowStyle Hidden -Wait -PassThru; " +
                     $"if ($setup.ExitCode -eq 0) {{ Start-Process -FilePath '{escapedExecutable}' }}";
        var encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-WindowStyle");
        startInfo.ArgumentList.Add("Hidden");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encodedScript);
        return Process.Start(startInfo) is not null;
    }

    private static void LogFailure(Exception exception)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassBar");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "update.log"),
                $"{DateTimeOffset.Now:O} {exception.GetType().Name}: {exception.Message}{Environment.NewLine}");
        }
        catch { }
    }
}
