namespace Crawl.Core.Messages;

/// <summary>
/// MassTransit message published by the API to request a new crawl.
/// </summary>
public record StartCrawlJob(Guid JobId, string Url, int MaxDepth = 2);
