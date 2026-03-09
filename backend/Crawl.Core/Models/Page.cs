namespace Crawl.Core.Models;

public class Page
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public string Url { get; set; } = string.Empty;

    /// <summary>Ratio of internal (same-domain) links to total qualifying links on the page.</summary>
    public double DomainLinkRatio { get; set; }

    /// <summary>
    /// All valid outgoing http/https links discovered on the page (normalised, deduplicated).
    /// Stored as a PostgreSQL text[] array.
    /// </summary>
    public string[] OutgoingLinks { get; set; } = Array.Empty<string>();

    public DateTime CrawledAt { get; set; } = DateTime.UtcNow;

    public Job Job { get; set; } = null!;
}
