namespace Crawl.Core.Models;

public enum JobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Canceled
}

public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FailureReason { get; set; }

    public ICollection<Page> Pages { get; set; } = new List<Page>();
    public ICollection<Edge> Edges { get; set; } = new List<Edge>();
}
