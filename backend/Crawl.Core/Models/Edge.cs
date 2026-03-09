namespace Crawl.Core.Models;

/// <summary>
/// Represents a directed link discovered during a crawl: ParentUrl → ChildUrl.
/// Only internal links (same domain as the seed URL) are recorded as edges.
/// </summary>
public class Edge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public string ParentUrl { get; set; } = string.Empty;
    public string ChildUrl { get; set; } = string.Empty;

    public Job Job { get; set; } = null!;
}
