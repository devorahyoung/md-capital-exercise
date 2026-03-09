namespace Crawl.Core.Messages;

/// <summary>
/// MassTransit message published by the Worker when a crawl job finishes.
/// </summary>
public record CrawlJobCompleted(Guid JobId, bool Success, string? ErrorMessage = null);
