using Crawl.Core.Models;

namespace Crawl.Api.Dtos;

public record PageDto(
    Guid Id,
    string Url,
    double DomainLinkRatio,
    string[] OutgoingLinks,
    DateTime CrawledAt);

public record EdgeDto(
    Guid Id,
    string ParentUrl,
    string ChildUrl);

public record JobDto(
    Guid Id,
    string Url,
    string Status,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? FailureReason,
    IEnumerable<PageDto> Pages,
    IEnumerable<EdgeDto> Edges,
    int PageCount = 0);

/// <summary>
/// A single node in the crawl-result tree returned by GET /api/jobs/{id}/tree.
/// Built server-side from the Edges table so the tree reflects the actual
/// discovered link graph rather than URL path segments.
/// </summary>
public record TreeNodeDto(
    string Url,
    double? DomainLinkRatio,
    IEnumerable<TreeNodeDto> Children);

/// <summary>Generic paginated response envelope returned by the history endpoint.</summary>
public record PaginatedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages);

public static class JobMappings
{
    public static PageDto ToDto(this Page p) =>
        new(p.Id, p.Url, p.DomainLinkRatio, p.OutgoingLinks, p.CrawledAt);

    public static EdgeDto ToDto(this Edge e) =>
        new(e.Id, e.ParentUrl, e.ChildUrl);

    public static JobDto ToDto(this Job j, int? pageCount = null) =>
        new(
            j.Id,
            j.Url,
            j.Status.ToString(),
            j.CreatedAt,
            j.StartedAt,
            j.CompletedAt,
            j.FailureReason,
            j.Pages.Select(p => p.ToDto()),
            j.Edges.Select(e => e.ToDto()),
            pageCount ?? j.Pages.Count);
}
