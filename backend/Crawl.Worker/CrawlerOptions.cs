namespace Crawl.Worker;

public class CrawlerOptions
{
    public const string SectionName = "Crawler";

    /// <summary>Maximum number of pages crawled per job.</summary>
    public int MaxPages { get; set; } = 200;

    /// <summary>Number of pages fetched concurrently in each BFS batch.</summary>
    public int ConcurrentFetches { get; set; } = 10;
}
