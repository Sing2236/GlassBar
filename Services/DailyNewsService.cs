using System.Net.Http;
using System.Xml.Linq;

namespace GlassBar.Services;

public sealed class DailyNewsService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(7) };
    private const string FeedUrl = "https://feeds.bbci.co.uk/news/rss.xml";

    public async Task<(string Title, string Link)> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var response = await Client.GetAsync(FeedUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var document = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
        var item = document.Descendants("item").FirstOrDefault();
        return item is null
            ? ("No headlines available", FeedUrl)
            : ((string?)item.Element("title") ?? "Latest news", (string?)item.Element("link") ?? FeedUrl);
    }
}
