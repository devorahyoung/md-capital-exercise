using Crawl.Core.Services;
using FluentAssertions;
using Xunit;

namespace Crawl.Tests;

/// <summary>
/// Integration-style tests that load real HTML fixture files from disk and run them
/// through <see cref="LinkExtractor"/>.
/// No network access or database required — the fixtures represent a mini static site.
///
/// Fixture layout (see Crawl.Tests/Fixtures/):
///   root.html  — home page with internal + external + non-qualifying links
///   about.html — inner page with only internal links
/// </summary>
public class CrawlerIntegrationTests
{
    private const string BaseDomain = "example.com";

    private static string LoadFixture(string fileName)
    {
        // Fixtures are copied to the output directory via the .csproj <None> entry.
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
        return File.ReadAllText(path);
    }

    // ── root.html fixture tests ───────────────────────────────────────────────

    /// <summary>
    /// root.html has 5 qualifying links (3 internal, 2 external).
    /// mailto:, tel:, javascript:, and #fragment must all be excluded from totalLinks.
    /// → ratio = 3/5 = 0.6
    /// </summary>
    [Fact]
    public void RootPage_ExcludesNonQualifyingSchemes_AndComputesCorrectRatio()
    {
        var html = LoadFixture("root.html");
        const string pageUrl = "https://example.com/";

        var (ratio, internalLinks) = LinkExtractor.Extract(html, pageUrl, BaseDomain);

        ratio.Should().Be(0.6,
            because: "root.html has 3 internal links (/about, /contact, www.example.com/blog) " +
                     "and 2 external links (github, twitter) — 3/5 = 0.6");

        internalLinks.Should().HaveCount(3);
        internalLinks.Should().Contain("https://example.com/about");
        internalLinks.Should().Contain("https://example.com/contact");
        // The stored URL keeps its original host (www.example.com); www-stripping is used
        // only for the domain comparison, not for rewriting stored URLs.
        internalLinks.Should().Contain("https://www.example.com/blog",
            because: "www.example.com/blog is classified as internal via www-stripping, " +
                     "but the stored URL retains the original www-prefixed host");
    }

    [Fact]
    public void RootPage_WwwVariantLink_TreatedAsInternal()
    {
        var html = LoadFixture("root.html");
        const string pageUrl = "https://example.com/";

        var (_, internalLinks) = LinkExtractor.Extract(html, pageUrl, BaseDomain);

        // www-stripping is used only for classification; the stored link keeps its original host.
        internalLinks.Should().Contain("https://www.example.com/blog",
            because: "https://www.example.com/blog strips to example.com for domain comparison " +
                     "and is therefore classified as internal");
    }

    // ── about.html fixture tests ──────────────────────────────────────────────

    /// <summary>
    /// about.html has 3 qualifying links (/, /contact, /blog) — all internal.
    /// #team and mailto: must be excluded.
    /// → ratio = 3/3 = 1.0
    /// </summary>
    [Fact]
    public void AboutPage_AllLinksInternal_ReturnsRatioOne()
    {
        var html = LoadFixture("about.html");
        const string pageUrl = "https://example.com/about";

        var (ratio, internalLinks) = LinkExtractor.Extract(html, pageUrl, BaseDomain);

        ratio.Should().Be(1.0,
            because: "about.html contains only internal hrefs once mailto: and #anchor are excluded");

        internalLinks.Should().HaveCount(3);
        internalLinks.Should().Contain("https://example.com");     // href="/"
        internalLinks.Should().Contain("https://example.com/contact");
        internalLinks.Should().Contain("https://example.com/blog");
    }

    // ── multi-page BFS simulation ─────────────────────────────────────────────

    /// <summary>
    /// Simulates a two-page crawl (root + about) without any HTTP calls, using a
    /// dictionary-backed fake fetcher.  Verifies BFS depth control and deduplication.
    /// </summary>
    [Fact]
    public async Task BfsCrawl_FollowsLinks_DeduplicatesUrls_AndRespectsMaxDepth()
    {
        // Arrange
        var rootHtml = LoadFixture("root.html");
        var aboutHtml = LoadFixture("about.html");

        // Fake HTTP fetch: maps URL → HTML content (null = 404)
        var site = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["https://example.com/"] = rootHtml,
            ["https://example.com/about"] = aboutHtml,
            ["https://example.com/contact"] = null,   // simulate a 404
            ["https://example.com/blog"] = null,       // simulate a 404
        };

        Task<string?> FakeFetch(string url)
        {
            // Strip trailing slash for lookup consistency
            var key = url.TrimEnd('/');
            if (!key.EndsWith("example.com") && key == "https://example.com")
                key = "https://example.com/";

            site.TryGetValue(url, out var html);
            return Task.FromResult(html);
        }

        // Act — BFS with maxDepth=1 against the fake site
        var results = await BfsCrawlAsync(
            seedUrl: "https://example.com/",
            maxDepth: 1,
            fetch: url => FakeFetch(url));

        // Assert
        results.Should().NotBeEmpty();

        var rootResult = results.FirstOrDefault(r => r.Url == "https://example.com/");
        rootResult.Should().NotBeNull();
        rootResult!.DomainLinkRatio.Should().Be(0.6,
            because: "root.html has 3 internal / 5 total qualifying links");

        var aboutResult = results.FirstOrDefault(r => r.Url == "https://example.com/about");
        aboutResult.Should().NotBeNull();
        aboutResult!.DomainLinkRatio.Should().Be(1.0,
            because: "about.html has all internal qualifying links");

        // The root URL must not be processed twice even though about.html links back to "/"
        results.Count(r => r.Url == "https://example.com/").Should().Be(1,
            because: "BFS must not revisit already-processed URLs");
    }

    // ── Minimal BFS implementation for integration testing ────────────────────
    // This is a self-contained BFS used only in tests to verify LinkExtractor's
    // output drives correct crawl behaviour without spinning up the Worker host.

    private sealed record CrawlResult(string Url, double DomainLinkRatio);

    private static async Task<List<CrawlResult>> BfsCrawlAsync(
        string seedUrl,
        int maxDepth,
        Func<string, Task<string?>> fetch,
        int maxPages = 50)
    {
        if (!Uri.TryCreate(seedUrl, UriKind.Absolute, out var seedUri))
            throw new ArgumentException($"Invalid seed URL: {seedUrl}");

        var baseDomain = LinkExtractor.StripWww(seedUri.Host);
        var results = new List<CrawlResult>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Url, int Depth)>();

        queue.Enqueue((seedUrl, 0));
        visited.Add(seedUrl);

        while (queue.Count > 0 && results.Count < maxPages)
        {
            var (currentUrl, depth) = queue.Dequeue();

            var html = await fetch(currentUrl);
            if (html is null) continue;

            var (ratio, childLinks) = LinkExtractor.Extract(html, currentUrl, baseDomain);
            results.Add(new CrawlResult(currentUrl, ratio));

            if (depth >= maxDepth) continue;

            foreach (var link in childLinks)
            {
                if (results.Count >= maxPages) break;
                if (visited.Add(link))
                    queue.Enqueue((link, depth + 1));
            }
        }

        return results;
    }
}
