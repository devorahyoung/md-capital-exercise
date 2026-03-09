using Crawl.Api.Data;
using Crawl.Core.Interfaces;
using Crawl.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Crawl.Api.Repositories;

public class JobRepository : IJobRepository
{
    private readonly AppDbContext _db;

    public JobRepository(AppDbContext db) => _db = db;

    public Task<Job?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        _db.Jobs
           .Include(j => j.Pages)
           .Include(j => j.Edges)
           .AsSplitQuery()
           .FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task<IEnumerable<Job>> GetAllAsync(CancellationToken ct = default) =>
        await _db.Jobs
            .Include(j => j.Pages)
            .Include(j => j.Edges)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct);

    public async Task<(IEnumerable<Job> Jobs, int TotalCount)> GetPagedAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = _db.Jobs.OrderByDescending(j => j.CreatedAt);
        var total = await query.CountAsync(ct);
        var jobs = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(j => j.Pages)
            .Include(j => j.Edges)
            .ToListAsync(ct);
        return (jobs, total);
    }

    public async Task<Job> CreateAsync(Job job, CancellationToken ct = default)
    {
        _db.Jobs.Add(job);
        await _db.SaveChangesAsync(ct);
        return job;
    }

    public async Task UpdateAsync(Job job, CancellationToken ct = default)
    {
        _db.Jobs.Update(job);
        await _db.SaveChangesAsync(ct);
    }
}
