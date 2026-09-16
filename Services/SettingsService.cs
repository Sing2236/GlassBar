using System.Text.Json;
using System.IO;
using GlassBar.Models;
using Microsoft.Win32;

namespace GlassBar.Services;

public sealed class SettingsService
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassBar");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public BarSettings Load()
    {
        BarSettings settings;
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<BarSettings>(File.ReadAllText(FilePath)) ?? new BarSettings();
            else
                settings = new BarSettings();
        }
        catch { settings = new BarSettings(); }

        settings.StartWithWindows = IsStartWithWindows();
        settings.CustomEffect ??= new CustomEffectConfig();
        settings.Stickers ??= [];
        settings.TopStickers ??= [];
        foreach (var sticker in settings.Stickers.Concat(settings.TopStickers))
            if (string.IsNullOrWhiteSpace(sticker.DisplayName) || sticker.DisplayName == "Sticker")
                sticker.DisplayName = Path.GetFileNameWithoutExtension(sticker.FilePath);
        return settings;
    }

    public void Save(BarSettings settings)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void SetStartWithWindows(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (enabled)
                key?.SetValue("GlassBar", $"\"{Environment.ProcessPath}\"");
            else
                key?.DeleteValue("GlassBar", throwOnMissingValue: false);
        }
        catch { }
    }

    private static bool IsStartWithWindows()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("GlassBar") is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch { return false; }
    }
}
