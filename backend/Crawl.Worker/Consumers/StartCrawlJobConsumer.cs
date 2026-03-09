using System.Text;
using Crawl.Core.Messages;
using Crawl.Core.Models;
using Crawl.Core.Services;
using Crawl.Worker.Data;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Crawl.Worker.Consumers;

public class StartCrawlJobConsumer : IConsumer<StartCrawlJob>
{
    private readonly WorkerDbContext _db;
    private readonly IPublishEndpoint _bus;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<StartCrawlJobConsumer> _logger;
    private readonly CrawlerOptions _options;
    private readonly string _connectionString;

    public StartCrawlJobConsumer(
        WorkerDbContext db,
        IPublishEndpoint bus,
        IHttpClientFactory httpFactory,
        ILogger<StartCrawlJobConsumer> logger,
        IOptions<CrawlerOptions> options)
    {
        _db = db;
        _bus = bus;
        _httpFactory = httpFactory;
        _logger = logger;
        _options = options.Value;
        _connectionString = db.Database.GetConnectionString()!;
    }

    public async Task Consume(ConsumeContext<StartCrawlJob> context)
    {
        var msg = context.Message;
        var ct = context.CancellationToken;

        _logger.LogInformation("Received crawl job {JobId} for {Url}", msg.JobId, msg.Url);

        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == msg.JobId, ct);
        if (job is null)
        {
            _logger.LogWarning("Job {JobId} not found – skipping.", msg.JobId);
            return;
        }

        // Guard 1: job was canceled before the worker picked up the message.
        if (job.Status == JobStatus.Canceled)
        {
            _logger.LogInformation("Job {JobId} was already canceled – skipping.", msg.JobId);
            return;
        }

        // ── Mark Running ──────────────────────────────────────────────────────
        job.Status = JobStatus.Running;
        job.StartedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var (pageCount, edgeCount) = await CrawlAsync(job.Id, msg.Url, msg.MaxDepth, conn, ct);

            // Guard 2: job was canceled while the BFS was running.
            // Reload the entity to get the latest status written by the API.
            await _db.Entry(job).ReloadAsync(ct);
            if (job.Status == JobStatus.Canceled)
            {
                _logger.LogInformation(
                    "Job {JobId} was canceled mid-run after {PageCount} pages – halting.",
                    job.Id, pageCount);
                return;
            }

            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            await _bus.Publish(new CrawlJobCompleted(job.Id, true), ct);
            _logger.LogInformation(
                "Job {JobId} completed – {PageCount} pages, {EdgeCount} edges.",
                job.Id, pageCount, edgeCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed.", job.Id);
            job.Status = JobStatus.Failed;
            job.FailureReason = ex.Message;
            job.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            await _bus.Publish(new CrawlJobCompleted(job.Id, false, ex.Message), ct);
        }
    }

    // ── Internal result type for a single fetched page ────────────────────────
    private sealed record PageFetchResult(
        string Url,
        int Depth,
        double DomainLinkRatio,
        List<string> ChildLinks,
        string[] AllLinks);

    // ── Concurrent BFS Crawler ────────────────────────────────────────────────
    // Each iteration drains up to ConcurrentFetches URLs from the queue and
    // fetches them all in parallel with Task.WhenAll. Results are merged
    // single-threaded (no locking needed) and flushed to the DB immediately
    // via ON CONFLICT DO NOTHING, replacing the end-of-job bulk write.
    private async Task<(int PageCount, int EdgeCount)> CrawlAsync(
        Guid jobId, string seedUrl, int maxDepth, NpgsqlConnection conn, CancellationToken ct)
    {
        if (!Uri.TryCreate(seedUrl, UriKind.Absolute, out var seedUri))
            throw new ArgumentException($"Invalid seed URL: {seedUrl}");

        var baseDomain = LinkExtractor.StripWww(seedUri.Host);
        var http = _httpFactory.CreateClient("CrawlClient");

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue   = new Queue<(string Url, int Depth)>();
        queue.Enqueue((seedUrl, 0));
        visited.Add(seedUrl);

        int totalPages = 0;
        int totalEdges = 0;

        while (queue.Count > 0 && totalPages < _options.MaxPages)
        {
            ct.ThrowIfCancellationRequested();

            // Guard 3: check the DB for an external cancel signal before each batch.
            var liveStatus = await _db.Jobs
                .AsNoTracking()
                .Where(j => j.Id == jobId)
                .Select(j => j.Status)
                .FirstOrDefaultAsync(ct);

            if (liveStatus == JobStatus.Canceled)
            {
                _logger.LogInformation("Job {JobId} canceled – stopping BFS after {PageCount} pages.", jobId, totalPages);
                return (totalPages, totalEdges);
            }

            // Drain up to ConcurrentFetches URLs from the queue into a batch.
            var batch = new List<(string Url, int Depth)>();
            while (queue.Count > 0
                   && batch.Count < _options.ConcurrentFetches
                   && totalPages + batch.Count < _options.MaxPages)
            {
                batch.Add(queue.Dequeue());
            }

            // Fetch all URLs in the batch concurrently.
            var results = await Task.WhenAll(batch.Select(async item =>
            {
                _logger.LogDebug("Crawling [{Depth}] {Url}", item.Depth, item.Url);
                var html = await FetchAsync(http, item.Url, ct);
                if (html is null) return null;
                var (ratio, childLinks, allLinks) = LinkExtractor.ExtractAll(html, item.Url, baseDomain);
                return new PageFetchResult(item.Url, item.Depth, ratio, childLinks, allLinks.ToArray());
            }));

            // Merge results and enqueue newly discovered children (single-threaded).
            var pageBatch = new List<(string Url, double Ratio, string[] Links)>();
            var edgeBatch = new List<(string ParentUrl, string ChildUrl)>();

            foreach (var r in results)
            {
                if (r is null) continue;

                pageBatch.Add((r.Url, r.DomainLinkRatio, r.AllLinks));
                foreach (var child in r.ChildLinks)
                    edgeBatch.Add((r.Url, child));

                if (r.Depth < maxDepth)
                {
                    foreach (var child in r.ChildLinks)
                    {
                        if (visited.Add(child))
                            queue.Enqueue((child, r.Depth + 1));
                    }
                }
            }

            // Flush this batch to the DB immediately.
            if (pageBatch.Count > 0)
                await FlushPagesAsync(pageBatch, jobId, conn, ct);
            if (edgeBatch.Count > 0)
                await FlushEdgesAsync(edgeBatch, jobId, conn, ct);

            totalPages += pageBatch.Count;
            totalEdges += edgeBatch.Count;
        }

        return (totalPages, totalEdges);
    }

    // ── Fetch HTML ────────────────────────────────────────────────────────────
    // ResponseHeadersRead lets us inspect Content-Type before downloading the
    // body, so non-HTML responses (PDFs, images, JS bundles) cost no bandwidth.
    private async Task<string?> FetchAsync(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HTTP {StatusCode} for {Url}", (int)response.StatusCode, url);
                return null;
            }

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Skipping non-HTML content ({ContentType}) at {Url}", contentType, url);
                return null;
            }

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Failed to fetch {Url}: {Message}", url, ex.Message);
            return null;
        }
    }

    // ── Batch DB flush helpers ────────────────────────────────────────────────
    // Raw Npgsql is used so we can emit ON CONFLICT DO NOTHING, which removes
    // the need for the pre-check SELECT queries that the old code required.

    private static async Task FlushPagesAsync(
        IReadOnlyList<(string Url, double Ratio, string[] Links)> batch,
        Guid jobId, NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand();
        cmd.Connection = conn;

        var sb = new StringBuilder(
            "INSERT INTO \"Pages\" (\"Id\", \"JobId\", \"Url\", \"DomainLinkRatio\", \"OutgoingLinks\", \"CrawledAt\") VALUES ");
        var now = DateTime.UtcNow;
        cmd.Parameters.AddWithValue("jobId", jobId);
        cmd.Parameters.AddWithValue("now", now);

        for (int i = 0; i < batch.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"(gen_random_uuid(), @jobId, @u{i}, @r{i}, @l{i}, @now)");
            cmd.Parameters.AddWithValue($"u{i}", batch[i].Url);
            cmd.Parameters.AddWithValue($"r{i}", batch[i].Ratio);
            cmd.Parameters.Add(new NpgsqlParameter($"l{i}", NpgsqlDbType.Array | NpgsqlDbType.Text)
                { Value = batch[i].Links });
        }

        sb.Append(" ON CONFLICT (\"JobId\", \"Url\") DO NOTHING");
        cmd.CommandText = sb.ToString();
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task FlushEdgesAsync(
        IReadOnlyList<(string ParentUrl, string ChildUrl)> batch,
        Guid jobId, NpgsqlConnection conn, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand();
        cmd.Connection = conn;

        var sb = new StringBuilder(
            "INSERT INTO \"Edges\" (\"Id\", \"JobId\", \"ParentUrl\", \"ChildUrl\") VALUES ");
        cmd.Parameters.AddWithValue("jobId", jobId);

        for (int i = 0; i < batch.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append($"(gen_random_uuid(), @jobId, @p{i}, @c{i})");
            cmd.Parameters.AddWithValue($"p{i}", batch[i].ParentUrl);
            cmd.Parameters.AddWithValue($"c{i}", batch[i].ChildUrl);
        }

        sb.Append(" ON CONFLICT (\"JobId\", \"ParentUrl\", \"ChildUrl\") DO NOTHING");
        cmd.CommandText = sb.ToString();
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
