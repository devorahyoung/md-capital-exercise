using Crawl.Api.Data;
using Crawl.Api.Dtos;
using Crawl.Core.Interfaces;
using Crawl.Core.Messages;
using Crawl.Core.Models;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crawl.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly IJobRepository _jobs;
    private readonly IPublishEndpoint _bus;
    private readonly AppDbContext _db;

    public JobsController(IJobRepository jobs, IPublishEndpoint bus, AppDbContext db)
    {
        _jobs = jobs;
        _bus = bus;
        _db = db;
    }

    // GET /api/jobs?page=1&pageSize=20
    // Uses a lightweight projection — no JOIN with Pages/Edges — to keep the list fast.
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var total = await _db.Jobs.CountAsync(ct);
        var totalPages = (int)Math.Ceiling((double)total / pageSize);

        var emptyPages = Array.Empty<PageDto>();
        var emptyEdges = Array.Empty<EdgeDto>();

        var items = await _db.Jobs
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new JobDto(
                j.Id,
                j.Url,
                j.Status.ToString(),
                j.CreatedAt,
                j.StartedAt,
                j.CompletedAt,
                j.FailureReason,
                emptyPages,
                emptyEdges,
                j.Pages.Count()))
            .ToListAsync(ct);

        return Ok(new PaginatedResult<JobDto>(items, total, page, pageSize, totalPages));
    }

    // GET /api/jobs/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var job = await _jobs.GetByIdAsync(id, ct);
        return job is null ? NotFound() : Ok(job.ToDto());
    }

    // GET /api/jobs/{id}/tree
    // Returns the crawl result as a hierarchical tree built from the Edges table.
    // Only available once the job has Completed; returns 400 for other statuses.
    [HttpGet("{id:guid}/tree")]
    public async Task<IActionResult> GetTree(Guid id, CancellationToken ct)
    {
        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return NotFound();

        if (job.Status != JobStatus.Completed)
            return BadRequest(new { error = "Tree is only available for completed jobs." });

        var pages = await _db.Pages
            .Where(p => p.JobId == id)
            .ToListAsync(ct);

        var edges = await _db.Edges
            .Where(e => e.JobId == id)
            .ToListAsync(ct);

        // Ratio lookup: URL → DomainLinkRatio (null if URL was discovered but not crawled)
        var ratioMap = pages.ToDictionary(
            p => p.Url,
            p => (double?)p.DomainLinkRatio,
            StringComparer.OrdinalIgnoreCase);

        // Children lookup: parentUrl → [childUrls]
        var childrenMap = edges
            .GroupBy(e => e.ParentUrl, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => e.ChildUrl)
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .ToList(),
                StringComparer.OrdinalIgnoreCase);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tree = BuildTreeNode(job.Url, ratioMap, childrenMap, visited);

        return Ok(tree);
    }

    private static TreeNodeDto BuildTreeNode(
        string url,
        Dictionary<string, double?> ratioMap,
        Dictionary<string, List<string>> childrenMap,
        HashSet<string> visited)
    {
        visited.Add(url);

        var children = new List<TreeNodeDto>();
        if (childrenMap.TryGetValue(url, out var childUrls))
        {
            // Only include children that were actually crawled (present in ratioMap).
            // Frontier URLs — links discovered on depth-N pages that point beyond the
            // crawl depth — appear as ChildUrls in edges but have no Pages entry,
            // so they would show as ratio-less orphan leaves. Filtering them out
            // keeps the tree to crawled pages only, matching the spec intent of
            // "discovered pages".
            //
            // Pre-claim ALL qualifying siblings in visited before recursing into any
            // of them. Without this, recursing into child A could re-encounter sibling B
            // (not yet visited) and descend into it again, producing unbounded depth
            // on densely cross-linked sites.
            var toProcess = childUrls
                .Where(c => ratioMap.ContainsKey(c) && visited.Add(c))
                .ToList();
            foreach (var childUrl in toProcess)
                children.Add(BuildTreeNode(childUrl, ratioMap, childrenMap, visited));
        }

        ratioMap.TryGetValue(url, out var ratio);
        return new TreeNodeDto(url, ratio, children);
    }

    // POST /api/jobs/{id}/cancel
    // Cancels a Pending or Running job by writing Canceled directly to the DB.
    // The worker polls the job status before each BFS batch and will stop as
    // soon as it sees the Canceled status, so no inter-service message is needed.
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return NotFound();

        if (job.Status != JobStatus.Pending && job.Status != JobStatus.Running)
            return Conflict(new { error = $"Cannot cancel a job with status '{job.Status}'." });

        job.Status = JobStatus.Canceled;
        job.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return Ok(job.ToDto());
    }

    // POST /api/jobs
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateJobRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Url))
            return BadRequest("Url is required.");

        var job = new Job { Url = request.Url };
        await _jobs.CreateAsync(job, ct);

        // Publish crawl command to RabbitMQ via MassTransit
        var maxDepth = request.MaxDepth is >= 1 and <= 5 ? request.MaxDepth : 2;
        await _bus.Publish(new StartCrawlJob(job.Id, job.Url, maxDepth), ct);

        return CreatedAtAction(nameof(GetById), new { id = job.Id }, job.ToDto());
    }
}

public record CreateJobRequest(string Url, int MaxDepth = 2);
