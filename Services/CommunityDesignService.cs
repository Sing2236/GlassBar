using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using GlassBar.Models;

namespace GlassBar.Services;

public sealed record CommunityImportResult(string Name, string Kind, bool Applied);

public sealed class CommunityDesignService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private static readonly string PackageFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassBar", "CommunityDesigns");

    public async Task<CommunityImportResult> ImportAsync(string command, BarSettings settings)
    {
        if (!Uri.TryCreate(command, UriKind.Absolute, out var protocol) || protocol.Scheme != "glassbar" || protocol.Host != "open")
            throw new InvalidOperationException("That is not a valid GlassBar design link.");
        var sourceValue = ParseQuery(protocol.Query).GetValueOrDefault("url");
        if (!Uri.TryCreate(sourceValue, UriKind.Absolute, out var source) ||
            (source.Scheme != Uri.UriSchemeHttps && !(source.Scheme == Uri.UriSchemeHttp && source.IsLoopback)))
            throw new InvalidOperationException("GlassBar only imports packages from HTTPS links.");
        if (!source.IsLoopback && !source.Host.Equals("get-glassbar.vercel.app", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That design host is not trusted by GlassBar.");

        using var response = await Client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 120_000) throw new InvalidOperationException("That design package is too large.");
        var bytes = await response.Content.ReadAsByteArrayAsync();
        if (bytes.Length > 120_000) throw new InvalidOperationException("That design package is too large.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        if (GetInt(root, "schemaVersion", 0) != 1) throw new InvalidOperationException("This package version is not supported.");
        var kind = GetString(root, "kind", "");
        if (kind is not ("glassbar" or "widget" or "animation")) throw new InvalidOperationException("This package type is not supported.");
        var name = root.TryGetProperty("metadata", out var metadata) ? GetString(metadata, "name", "Community design") : "Community design";

        Directory.CreateDirectory(PackageFolder);
        var fileName = string.Concat(name.Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or ' ')).Trim();
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "Community design";
        await File.WriteAllBytesAsync(Path.Combine(PackageFolder, $"{fileName[..Math.Min(fileName.Length, 60)]}.glassbar.json"), bytes);

        var applied = kind switch
        {
            "glassbar" => ApplyGlassBar(root, settings),
            "animation" => ApplyAnimation(root, settings),
            _ => false
        };
        if (applied) new SettingsService().Save(settings);
        return new CommunityImportResult(name[..Math.Min(name.Length, 60)], kind, applied);
    }

    private static bool ApplyGlassBar(JsonElement root, BarSettings settings)
    {
        if (!root.TryGetProperty("glassbar", out var bar)) return false;
        settings.BarOrientation = GetString(bar, "orientation", "horizontal") == "vertical" ? "Vertical" : "Horizontal";
        settings.BarWidth = Math.Clamp(GetDouble(bar, "width", settings.BarWidth), 520, 1600);
        settings.BarHeight = Math.Clamp(GetDouble(bar, "height", settings.BarHeight), 48, 120);
        settings.CornerRadius = Math.Clamp(GetDouble(bar, "radius", settings.CornerRadius), 0, 48);
        settings.Opacity = Math.Clamp(GetDouble(bar, "opacity", settings.Opacity * 100) / 100, .05, 1);
        settings.BackgroundVisible = GetString(bar, "backgroundMode", "glass") != "transparent";
        settings.Effect = GetString(bar, "effectMode", "ambient") == "none" ? "Off" : "Rain";
        settings.CommunityAnimationActive = false;
        settings.Shadow = Math.Clamp(GetDouble(bar, "shadow", settings.Shadow), 0, 80);
        var accent = GetString(bar, "accent", settings.Accent);
        if (HexColor.IsMatch(accent)) settings.Accent = accent;
        var backgroundStart = GetString(bar, "backgroundStart", settings.BackgroundStart);
        if (HexColor.IsMatch(backgroundStart)) settings.BackgroundStart = backgroundStart;
        var backgroundEnd = GetString(bar, "backgroundEnd", settings.BackgroundEnd);
        if (HexColor.IsMatch(backgroundEnd)) settings.BackgroundEnd = backgroundEnd;
        var border = GetString(bar, "border", settings.Border);
        if (HexColor.IsMatch(border)) settings.Border = border;
        if (root.TryGetProperty("animation", out _)) ApplyAnimation(root, settings);
        return true;
    }

    private static bool ApplyAnimation(JsonElement root, BarSettings settings)
    {
        if (!root.TryGetProperty("animation", out var animation)) return false;
        settings.Effect = "Custom";
        settings.CommunityAnimationActive = true;
        settings.CustomEffect.Name = GetString(animation, "name", "Community effect")[..Math.Min(GetString(animation, "name", "Community effect").Length, 40)];
        settings.CustomEffect.Shape = GetString(animation, "shape", "orb") switch { "line" => "Line", "spark" => "Spark", _ => "Orb" };
        settings.CustomEffect.Motion = GetString(animation, "motion", "drift") switch { "fall" => "Fall", "rise" => "Rise", _ => "Drift" };
        settings.CustomEffect.Density = Percent(animation, "density", .48);
        settings.CustomEffect.Speed = Percent(animation, "speed", .56);
        settings.CustomEffect.Size = Percent(animation, "size", .42);
        settings.CustomEffect.Glow = Percent(animation, "glow", .28);
        settings.CustomEffect.Trail = Percent(animation, "trail", .62);
        var secondary = GetString(animation, "secondaryColor", settings.CustomEffect.SecondaryColor);
        if (HexColor.IsMatch(secondary)) settings.CustomEffect.SecondaryColor = secondary;
        var primary = GetString(animation, "primaryColor", settings.Accent);
        if (HexColor.IsMatch(primary)) settings.Accent = primary;
        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .Where(part => part.Length == 2)
        .ToDictionary(part => Uri.UnescapeDataString(part[0]), part => Uri.UnescapeDataString(part[1]), StringComparer.OrdinalIgnoreCase);
    private static string GetString(JsonElement element, string name, string fallback) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static double GetDouble(JsonElement element, string name, double fallback) => element.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) ? number : fallback;
    private static int GetInt(JsonElement element, string name, int fallback) => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;
    private static double Percent(JsonElement element, string name, double fallback) => Math.Clamp(GetDouble(element, name, fallback * 100) / 100, 0, 1);
}
