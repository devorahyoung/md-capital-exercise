using Crawl.Core.Models;

namespace Crawl.Core.Interfaces;

public interface IJobRepository
{
    Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IEnumerable<Job>> GetAllAsync(CancellationToken ct = default);
    Task<(IEnumerable<Job> Jobs, int TotalCount)> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);
    Task<Job> CreateAsync(Job job, CancellationToken ct = default);
    Task UpdateAsync(Job job, CancellationToken ct = default);
}
