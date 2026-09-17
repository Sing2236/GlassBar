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
            if (manifest is null) throw new InvalidDataException("The update manifest is empty.");
            if (!UpdateManifestSignature.Verify(manifest))
                throw new CryptographicException("The update manifest signature is invalid.");
            if (!Version.TryParse(manifest.Version, out var availableVersion))
                throw new InvalidDataException("The update manifest version is invalid.");

            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            if (availableVersion <= currentVersion) return false;
            if (!TryGetInstallerUri(manifest.InstallerUrl, out var installerUri))
                throw new InvalidDataException("The update installer URL is not trusted.");

            var installerPath = await DownloadAndVerifyAsync(installerUri, availableVersion, manifest.Sha256);
            LogStatus($"Update {availableVersion} downloaded and verified. Starting the installer.");
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

    private static async Task<string> DownloadAndVerifyAsync(Uri installerUri, Version version, string expectedSha256)
    {
        if (expectedSha256.Length != 64 || expectedSha256.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The update checksum is invalid.");

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
                throw new CryptographicException("The downloaded installer checksum does not match the signed manifest.");
            }
        }

        File.Move(partialPath, installerPath, true);
        return installerPath;
    }

    private static bool LaunchUpdateBootstrap(string installerPath)
    {
        var defaultExecutable = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "GlassBar", "GlassBar.exe");
        var currentExecutable = Environment.ProcessPath ?? defaultExecutable;
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassBar", "update.log");
        var escapedInstaller = EscapePowerShellLiteral(installerPath);
        var escapedExecutable = EscapePowerShellLiteral(currentExecutable);
        var escapedLogPath = EscapePowerShellLiteral(logPath);
        var script =
            "$ErrorActionPreference = 'Stop'; " +
            $"$logPath = '{escapedLogPath}'; " +
            "function Write-UpdateLog([string]$message) { Add-Content -LiteralPath $logPath -Value ((Get-Date).ToString('o') + ' ' + $message) }; " +
            $"$fallbackExecutable = '{escapedExecutable}'; " +
            $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; " +
            "Get-Process -Name 'GlassBar' -ErrorAction SilentlyContinue | Wait-Process -Timeout 30 -ErrorAction SilentlyContinue; " +
            "try { " +
            $"$setup = Start-Process -FilePath '{escapedInstaller}' " +
            "-ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CLOSEAPPLICATIONS','/FORCECLOSEAPPLICATIONS' " +
            "-WindowStyle Hidden -Wait -PassThru; " +
            "if ($setup.ExitCode -ne 0) { throw ('Installer exited with code ' + $setup.ExitCode) }; " +
            "$installKey = 'Registry::HKEY_CURRENT_USER\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{5E1D5A44-7E7B-4EA0-9C4B-6715C67B19C4}_is1'; " +
            "$installLocation = (Get-ItemProperty -LiteralPath $installKey -ErrorAction SilentlyContinue).InstallLocation; " +
            "$installedExecutable = if ($installLocation) { Join-Path $installLocation 'GlassBar.exe' } else { $fallbackExecutable }; " +
            "if (-not (Test-Path -LiteralPath $installedExecutable)) { throw 'The installed GlassBar executable was not found.' }; " +
            "Write-UpdateLog ('Update installed successfully. Launching ' + $installedExecutable); " +
            "Start-Process -FilePath $installedExecutable " +
            "} catch { " +
            "Write-UpdateLog ('Update bootstrap failed: ' + $_.Exception.Message); " +
            "if (Test-Path -LiteralPath $fallbackExecutable) { Start-Process -FilePath $fallbackExecutable }; exit 1 }";
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

    private static string EscapePowerShellLiteral(string value) => value.Replace("'", "''");

    private static void LogFailure(Exception exception)
        => LogStatus($"{exception.GetType().Name}: {exception.Message}");

    private static void LogStatus(string message)
    {
        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassBar");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "update.log"),
                $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
