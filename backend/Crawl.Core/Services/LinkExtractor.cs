using HtmlAgilityPack;

namespace Crawl.Core.Services;

/// <summary>
/// Parses the outgoing links from an HTML page and computes the Domain Link Ratio.
/// Extracted into Crawl.Core so it can be unit-tested independently of the Worker host.
/// </summary>
public static class LinkExtractor
{
    /// <summary>
    /// Parses <paramref name="html"/> and returns the Domain Link Ratio together with the
    /// deduplicated list of <em>internal</em> links (for BFS enqueuing).
    /// Delegates to <see cref="ExtractAll"/> and discards the all-links list.
    /// </summary>
    public static (double DomainLinkRatio, List<string> InternalLinks) Extract(
        string html, string pageUrl, string baseDomain)
    {
        var (ratio, internalLinks, _) = ExtractAll(html, pageUrl, baseDomain);
        return (ratio, internalLinks);
    }

    /// <summary>
    /// Parses <paramref name="html"/>, resolves every qualifying link to an absolute URL,
    /// and returns:
    /// <list type="bullet">
    ///   <item><description>DomainLinkRatio — internal / total (0.0 when no qualifying links)</description></item>
    ///   <item><description>InternalLinks   — unique same-domain links, used to drive BFS</description></item>
    ///   <item><description>AllOutgoingLinks — unique valid http/https links (internal + external), stored per page</description></item>
    /// </list>
    /// </summary>
    public static (double DomainLinkRatio, List<string> InternalLinks, List<string> AllOutgoingLinks) ExtractAll(
        string html, string pageUrl, string baseDomain)
    {
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out var pageUri))
            return (0.0, new List<string>(), new List<string>());

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Collect href values, excluding non-navigable schemes and fragment-only links up front.
        var anchors = doc.DocumentNode
            .SelectNodes("//a[@href]")?
            .Select(n => n.GetAttributeValue("href", "").Trim())
            .Where(h => !string.IsNullOrEmpty(h)
                        && !h.StartsWith('#')
                        && !h.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                        && !h.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                        && !h.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? new List<string>();

        int totalLinks = anchors.Count;

        // Spec: "If a page has zero outgoing links, ratio is 0."
        if (totalLinks == 0)
            return (0.0, new List<string>(), new List<string>());

        var seenInternal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenAll      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // rawInternalCount tracks every qualifying occurrence of an internal link,
        // including duplicates, to match the intended ratio semantics (3 occurrences
        // of the same internal URL still count as 3 in the numerator).
        int rawInternalCount = 0;

        foreach (var href in anchors)
        {
            // Resolve relative URLs against the page URL.
            if (!Uri.TryCreate(pageUri, href, out var absoluteUri)) continue;

            // Ignore non-http(s) schemes that slipped through (e.g. ftp:, data:).
            if (absoluteUri.Scheme != "http" && absoluteUri.Scheme != "https") continue;

            // Normalise: strip fragment, strip trailing slash.
            var normalized = absoluteUri.GetLeftPart(UriPartial.Query).TrimEnd('/');

            seenAll.Add(normalized);

            if (StripWww(absoluteUri.Host).Equals(baseDomain, StringComparison.OrdinalIgnoreCase))
            {
                rawInternalCount++;
                seenInternal.Add(normalized);
            }
        }

        double ratio = (double)rawInternalCount / totalLinks;

        return (Math.Round(ratio, 4), seenInternal.ToList(), seenAll.ToList());
    }

    /// <summary>Strips a leading "www." from a hostname for domain comparison.</summary>
    public static string StripWww(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
}
