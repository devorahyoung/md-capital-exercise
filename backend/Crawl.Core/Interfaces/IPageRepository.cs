using Crawl.Core.Models;

namespace Crawl.Core.Interfaces;

public interface IPageRepository
{
    Task<IEnumerable<Page>> GetByJobIdAsync(Guid jobId, CancellationToken ct = default);
    Task<Page> CreateAsync(Page page, CancellationToken ct = default);
}
