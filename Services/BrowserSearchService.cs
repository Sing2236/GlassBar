using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using GlassBar.Models;

namespace GlassBar.Services;

public sealed class BrowserSearchService
{
    private sealed record BrowserInfo(string Name, string? Executable, string? ProfileRoot, bool IsFirefox);

    public Task<IReadOnlyList<BrowserSuggestion>> SearchHistoryAsync(string query, CancellationToken cancellationToken) =>
        Task.Run(() => SearchHistory(query.Trim(), cancellationToken), cancellationToken);

    public string BuildSearchUrl(string query)
    {
        var browser = GetDefaultBrowser();
        var template = TryGetChromiumSearchTemplate(browser);
        if (!string.IsNullOrWhiteSpace(template))
            return template.Replace("{searchTerms}", Uri.EscapeDataString(query), StringComparison.OrdinalIgnoreCase);

        var escaped = Uri.EscapeDataString(query);
        return browser.Name switch
        {
            "Edge" => $"https://www.bing.com/search?q={escaped}",
            "Firefox" => $"https://www.google.com/search?q={escaped}",
            _ => $"https://www.google.com/search?q={escaped}"
        };
    }

    public void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static IReadOnlyList<BrowserSuggestion> SearchHistory(string query, CancellationToken cancellationToken)
    {
        var browser = GetDefaultBrowser();
        var database = FindHistoryDatabase(browser);
        if (database is null) return [];

        var temporary = Path.Combine(Path.GetTempPath(), $"glassbar-history-{Guid.NewGuid():N}.db");
        try
        {
            using (var input = new FileStream(database, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                input.CopyTo(output);

            cancellationToken.ThrowIfCancellationRequested();
            using var connection = new SqliteConnection($"Data Source={temporary};Mode=ReadOnly");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = browser.IsFirefox
                ? "SELECT COALESCE(title, ''), url FROM moz_places WHERE url LIKE 'http%' AND " +
                  "($query = '' OR title LIKE $match OR url LIKE $match) ORDER BY visit_count DESC, last_visit_date DESC LIMIT 10"
                : "SELECT COALESCE(title, ''), url FROM urls WHERE url LIKE 'http%' AND " +
                  "($query = '' OR title LIKE $match OR url LIKE $match) ORDER BY visit_count DESC, last_visit_time DESC LIMIT 10";
            command.Parameters.AddWithValue("$query", query);
            command.Parameters.AddWithValue("$match", $"%{query}%");

            var results = new List<BrowserSuggestion>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var url = reader.GetString(1);
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    uri.Scheme is not ("http" or "https")) continue;
                var title = reader.GetString(0).Trim();
                results.Add(new BrowserSuggestion(string.IsNullOrWhiteSpace(title) ? uri.Host : title, url));
            }
            return results.GroupBy(result => result.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()).ToList();
        }
        catch (OperationCanceledException) { throw; }
        catch { return []; }
        finally
        {
            try { File.Delete(temporary); } catch { }
        }
    }

    private static string? FindHistoryDatabase(BrowserInfo browser)
    {
        if (string.IsNullOrWhiteSpace(browser.ProfileRoot) || !Directory.Exists(browser.ProfileRoot)) return null;
        if (browser.IsFirefox)
            return Directory.EnumerateDirectories(browser.ProfileRoot)
                .Select(path => Path.Combine(path, "places.sqlite"))
                .FirstOrDefault(File.Exists);

        return EnumerateChromiumProfiles(browser.ProfileRoot)
            .Select(path => Path.Combine(path, "History"))
            .FirstOrDefault(File.Exists);
    }

    private static string? TryGetChromiumSearchTemplate(BrowserInfo browser)
    {
        if (browser.IsFirefox || string.IsNullOrWhiteSpace(browser.ProfileRoot)) return null;
        foreach (var profile in EnumerateChromiumProfiles(browser.ProfileRoot))
        {
            var preferences = Path.Combine(profile, "Preferences");
            if (!File.Exists(preferences)) continue;
            try
            {
                using var stream = File.Open(preferences, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var document = JsonDocument.Parse(stream);
                if (document.RootElement.TryGetProperty("default_search_provider_data", out var provider) &&
                    provider.TryGetProperty("template_url_data", out var template) &&
                    template.TryGetProperty("url", out var url))
                    return url.GetString();
            }
            catch { }
        }
        return null;
    }

    private static IEnumerable<string> EnumerateChromiumProfiles(string root)
    {
        var preferred = new[] { "Default" }.Concat(Directory.EnumerateDirectories(root, "Profile *")
            .Select(Path.GetFileName).Where(name => name is not null)!);
        foreach (var name in preferred)
        {
            var path = Path.Combine(root, name!);
            if (Directory.Exists(path)) yield return path;
        }
    }

    private static BrowserInfo GetDefaultBrowser()
    {
        string? progId = null;
        try
        {
            using var choice = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            progId = choice?.GetValue("ProgId") as string;
        }
        catch { }

        var name = progId?.Contains("MSEdge", StringComparison.OrdinalIgnoreCase) == true ? "Edge" :
            progId?.Contains("Firefox", StringComparison.OrdinalIgnoreCase) == true ? "Firefox" : "Chrome";
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profile = name switch
        {
            "Edge" => Path.Combine(local, "Microsoft", "Edge", "User Data"),
            "Firefox" => Path.Combine(roaming, "Mozilla", "Firefox", "Profiles"),
            _ => Path.Combine(local, "Google", "Chrome", "User Data")
        };

        string? executable = null;
        try
        {
            using var command = Registry.ClassesRoot.OpenSubKey($@"{progId}\shell\open\command");
            var value = command?.GetValue(null) as string;
            if (!string.IsNullOrWhiteSpace(value))
                executable = value.StartsWith('"') ? value.Split('"')[1] : value.Split(' ')[0];
        }
        catch { }
        return new BrowserInfo(name, executable, profile, name == "Firefox");
    }
}
