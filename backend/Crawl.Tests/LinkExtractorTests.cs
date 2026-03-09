using Crawl.Core.Services;
using FluentAssertions;
using Xunit;

namespace Crawl.Tests;

/// <summary>
/// Unit tests for <see cref="LinkExtractor"/>.
/// Covers Domain Link Ratio calculation and URL normalisation.
/// No network access required — all tests use inline HTML strings.
/// </summary>
public class LinkExtractorTests
{
    private const string BaseDomain = "example.com";
    private const string PageUrl = "https://example.com/";

    // ── Domain Link Ratio ─────────────────────────────────────────────────────

    [Fact]
    public void ZeroOutgoingLinks_ReturnsRatioZero()
    {
        // Spec: "If a page has zero outgoing links, ratio is 0."
        const string html = "<html><body><p>No links here.</p></body></html>";

        var (ratio, links) = LinkExtractor.Extract(html, PageUrl, BaseDomain);

        ratio.Should().Be(0.0);
        links.Should().BeEmpty();
    }

    [Fact]
    public void PageWithOnlyFragmentLinks_ReturnsRatioZero()
    {
        // Fragment-only hrefs must not be counted in totalLinks.
        const string html = "<html><body><a href='#top'>Top</a><a href='#footer'>Footer</a></body></html>";

        var (ratio, links) = LinkExtractor.Extract(html, PageUrl, BaseDomain);

        ratio.Should().Be(0.0);
        links.Should().BeEmpty();
    }

    [Fact]
    public void AllLinksInternal_ReturnsRatioOne()
    {
        const string html = @"<html><body>
            <a href='/about'>About</a>
            <a href='/contact'>Contact</a>
            <a href='https://example.com/blog'>Blog</a>
        </body></html>";

        var (ratio, _) = LinkExtractor.Extract(html, PageUrl, BaseDomain);

        ratio.Should().Be(1.0);
    }

    [Fact]
    public void AllLinksExternal_ReturnsRatioZero()
    {
        const string html = @"<html><body>
            <a href='https://github.com'>GitHub</a>
            <a href='https://google.com'>Google</a>
        </body></html>";

        var (ratio, links) = LinkExtractor.Extract(html, PageUrl, BaseDomain);

        ratio.Should().Be(0.0);
        links.Should().BeEmpty();
    }

    [Fact]
    public void MixedLinks_ReturnsCorrectRatio()
    {
        // 2 internal, 3 external → ratio = 2/5 = 0.4
        const string html = @"<html><body>
            <a href='/about'>About</a>
            <a href='/jobs'>Jobs</a>
            <a href='https://twitter.com/ex'>Twitter</a>
            <a href='https://linkedin.com/ex'>LinkedIn</a>
            <a href='https://github.com/ex'>GitHub</a>
        </body></html>";

        var (ratio, links) = LinkExtractor.Extract(html, PageUrl, BaseDomain);

        ratio.Should().Be(0.4);
        links.Should().HaveCount(2);
    }

    [Fact]
    public void MailtoLinks_AreIgnored_NotCountedInTotal()
    {
        // mailto: must not count towards totalLinks at all.
        const string html = @"<html><body>
            <a href='mailto:info@example.com'>Email</a>
            <a href='/about'>About</a>
        </body></html>";

        var (ratio, links) = LinkExtractor.Extract(html, PageUrl, BaseDomain);

        // Only 1 qualifying link (/about), 1 internal → ratio = 1.0
        ratio.Should().Be(1.0);
        links.Should().ContainSingle();
    }

    [Fact]
    public void TelLinks_AreIgnored_NotCountedInTotal()
    {
        const string html = @"<html><body>
            <a href='tel:+15551234567'>Call us</a>
            <a href='https://external.com'>External</a>
        </body></html>";

        var (ratio, links) = LinkExtractor.Extract(html, PageUrl, BaseDomain);

        // Only 1 qualifying link (external.com), 0 internal → ratio = 0.0
        ratio.Should().Be(0.0);
        links.Should().BeEmpty();
    }

    [Fact]
    public void JavascriptLinks_AreIgnored_NotCountedInTotal()
    {
        const string html = @"<html><body>
            <a href='javascript:void(0)'>Click</a>
            <a href='/page'>Page</a>
        </body></html>";

        var (ratio, links) = LinkExtractor.Extract(html, PageUrl, BaseDomain);

        // Only 1 qualifying link (/page), 1 internal → ratio = 1.0
        ratio.Should().Be(1.0);
        links.Should().ContainSingle();
    }

    // ── www. normalisation ────────────────────────────────────────────────────

    [Fact]
    public void WwwVariantLinks_TreatedAsInternal()
    {
        // "www.example.com" must be treated as the same domain as "example.com".
        const string html = @"<html><body>
            <a href='https://www.example.com/about'>About (www)</a>
            <a href='https://other.com'>Other</a>
        </body></html>";

        var (ratio, links) = LinkExtractor.Extract(html, PageUrl, BaseDomain);

        // 1 internal (www variant), 1 external → ratio = 0.5
        ratio.Should().Be(0.5);
        links.Should().ContainSingle(l => l.Contains("about"));
    }

    [Fact]
    public void StripWww_RemovesLeadingWww()
    {
        LinkExtractor.StripWww("www.example.com").Should().Be("example.com");
    }

    [Fact]
    public void StripWww_LeavesNonWwwHostUnchanged()
    {
        LinkExtractor.StripWww("example.com").Should().Be("example.com");
        LinkExtractor.StripWww("sub.example.com").Should().Be("sub.example.com");
    }

    [Fact]
    public void StripWww_IsCaseInsensitive()
    {
        LinkExtractor.StripWww("WWW.Example.COM").Should().Be("Example.COM");
    }

    // ── URL normalisation ─────────────────────────────────────────────────────

    [Fact]
    public void RelativeUrl_IsResolvedToAbsolute()
    {
        const string html = "<html><body><a href='/products/item'>Item</a></body></html>";
        const string pageUrl = "https://example.com/shop/";

        var (_, links) = LinkExtractor.Extract(html, pageUrl, BaseDomain);

        links.Should().ContainSingle()
             .Which.Should().Be("https://example.com/products/item");
    }

    [Fact]
    public void RelativeUrlSamePage_IsResolvedCorrectly()
    {
        const string html = "<html><body><a href='../other'>Other</a></body></html>";
        const string pageUrl = "https://example.com/foo/bar/";

        var (_, links) = LinkExtractor.Extract(html, pageUrl, BaseDomain);

        links.Should().ContainSingle()
             .Which.Should().Be("https://example.com/foo/other");
    }

    [Fact]
    public void FragmentInAbsoluteUrl_IsStripped()
    {
        // "https://example.com/page#section" and "https://example.com/page" must
        // normalise to the same URL ("https://example.com/page").
        const string html = @"<html><body>
            <a href='https://example.com/page#section'>Section</a>
            <a href='https://example.com/page'>Page</a>
        </body></html>";

        var (_, links) = LinkExtractor.Extract(html, PageUrl, BaseDomain);

        // After deduplication there should be one unique internal URL.
        links.Should().ContainSingle()
             .Which.Should().Be("https://example.com/page");
    }

    [Fact]
    public void TrailingSlash_IsStrippedForNormalisation()
    {
        // "https://example.com/about/" and "https://example.com/about" must deduplicate.
        const string html = @"<html><body>
            <a href='/about/'>About (slash)</a>
            <a href='/about'>About (no slash)</a>
        </body></html>";

        var (_, links) = LinkExtractor.Extract(html, PageUrl, BaseDomain);

        links.Should().ContainSingle()
             .Which.Should().Be("https://example.com/about");
    }

    [Fact]
    public void DuplicateInternalLinks_AreDeduplicated()
    {
        const string html = @"<html><body>
            <a href='/about'>About</a>
            <a href='/about'>About again</a>
            <a href='https://example.com/about'>About (absolute)</a>
        </body></html>";

        var (ratio, links) = LinkExtractor.Extract(html, PageUrl, BaseDomain);

        // 3 total links, all internal, but deduplicated to 1 unique URL.
        // Ratio is calculated on total before dedup (3/3 = 1).
        ratio.Should().Be(1.0);
        links.Should().ContainSingle();
    }

    [Fact]
    public void InvalidPageUrl_ReturnsZeroRatioAndEmptyLinks()
    {
        const string html = "<html><body><a href='/about'>About</a></body></html>";

        var (ratio, links) = LinkExtractor.Extract(html, "not-a-valid-url", BaseDomain);

        ratio.Should().Be(0.0);
        links.Should().BeEmpty();
    }

    [Fact]
    public void EmptyHtml_ReturnsZeroRatioAndEmptyLinks()
    {
        var (ratio, links) = LinkExtractor.Extract(string.Empty, PageUrl, BaseDomain);

        ratio.Should().Be(0.0);
        links.Should().BeEmpty();
    }

    [Fact]
    public void RatioIsRoundedToFourDecimalPlaces()
    {
        // 1 internal, 3 total → 0.3333...
        const string html = @"<html><body>
            <a href='/about'>About</a>
            <a href='https://ext1.com'>Ext1</a>
            <a href='https://ext2.com'>Ext2</a>
        </body></html>";

        var (ratio, _) = LinkExtractor.Extract(html, PageUrl, BaseDomain);

        ratio.Should().Be(0.3333);
    }
}
