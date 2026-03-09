using Crawl.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Crawl.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<Edge> Edges => Set<Edge>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Job>(e =>
        {
            e.HasKey(j => j.Id);
            e.Property(j => j.Url).IsRequired().HasMaxLength(2048);
            e.Property(j => j.Status).HasConversion<string>();
            e.Property(j => j.FailureReason).HasMaxLength(2048);
            e.HasMany(j => j.Pages)
             .WithOne(p => p.Job)
             .HasForeignKey(p => p.JobId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(j => j.Edges)
             .WithOne(edge => edge.Job)
             .HasForeignKey(edge => edge.JobId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Page>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Url).IsRequired().HasMaxLength(2048);
            e.Property(p => p.OutgoingLinks).HasColumnType("text[]");
            // Unique constraint: the same URL must not be stored twice for the same job.
            // Acts as the DB-level idempotency guard if the message is redelivered.
            e.HasIndex(p => new { p.JobId, p.Url })
             .IsUnique()
             .HasDatabaseName("UX_Pages_JobId_Url");
        });

        modelBuilder.Entity<Edge>(e =>
        {
            e.HasKey(edge => edge.Id);
            e.Property(edge => edge.ParentUrl).IsRequired().HasMaxLength(2048);
            e.Property(edge => edge.ChildUrl).IsRequired().HasMaxLength(2048);
            // Unique constraint: no duplicate parent→child edge per job.
            e.HasIndex(edge => new { edge.JobId, edge.ParentUrl, edge.ChildUrl })
             .IsUnique()
             .HasDatabaseName("UX_Edges_JobId_ParentUrl_ChildUrl");
        });
    }
}
