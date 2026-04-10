using Microsoft.EntityFrameworkCore;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Data;

// AppDbContext is EF Core's gateway to the database.
// It translates LINQ queries into SQL and maps rows back to C# objects.
public class AppDbContext : DbContext
{
    // The options (connection string, provider) are injected by the DI container.
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Each DbSet<T> maps to one table and lets you query/insert/update that table.
    public DbSet<Pipeline> Pipelines => Set<Pipeline>();
    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();
    public DbSet<RunError> RunErrors => Set<RunError>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── pipelines table ──────────────────────────────────────────────────────
        modelBuilder.Entity<Pipeline>(e =>
        {
            e.ToTable("pipelines");
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasColumnName("id").UseIdentityColumn();
            e.Property(p => p.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
            e.Property(p => p.Description).HasColumnName("description").HasMaxLength(500);
            e.Property(p => p.SourceSystem).HasColumnName("source_system").HasMaxLength(200).IsRequired();
            e.Property(p => p.SinkSystem).HasColumnName("sink_system").HasMaxLength(200).IsRequired();
            e.Property(p => p.CreatedAt).HasColumnName("created_at");
            e.HasIndex(p => p.Name).IsUnique();
        });

        // ── pipeline_runs table ──────────────────────────────────────────────────
        modelBuilder.Entity<PipelineRun>(e =>
        {
            e.ToTable("pipeline_runs");
            e.HasKey(r => r.Id);
            e.Property(r => r.Id).HasColumnName("id").UseIdentityColumn();
            e.Property(r => r.PipelineId).HasColumnName("pipeline_id");
            e.Property(r => r.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(r => r.TriggeredBy).HasColumnName("triggered_by").HasMaxLength(200);
            e.Property(r => r.StartedAt).HasColumnName("started_at");
            e.Property(r => r.EndedAt).HasColumnName("completed_at");   // DB column name
            e.Property(r => r.RecordsRead).HasColumnName("records_read");
            e.Property(r => r.RecordsWritten).HasColumnName("records_written");
            e.Property(r => r.RecordsFailed).HasColumnName("records_failed");
            // ErrorRate is computed in C# — tell EF Core to ignore it
            e.Ignore(r => r.ErrorRate);
            e.HasOne(r => r.Pipeline)
             .WithMany(p => p.Runs)
             .HasForeignKey(r => r.PipelineId);
        });

        // ── run_errors table ─────────────────────────────────────────────────────
        modelBuilder.Entity<RunError>(e =>
        {
            e.ToTable("run_errors");
            e.HasKey(err => err.Id);
            e.Property(err => err.Id).HasColumnName("id").UseIdentityColumn();
            e.Property(err => err.RunId).HasColumnName("run_id");
            e.Property(err => err.ErrorCode).HasColumnName("error_code").HasMaxLength(100).IsRequired();
            e.Property(err => err.Message).HasColumnName("message").HasMaxLength(2000).IsRequired();
            e.Property(err => err.OccurredAt).HasColumnName("occurred_at");
            e.HasOne(err => err.Run)
             .WithMany(r => r.Errors)
             .HasForeignKey(err => err.RunId);
        });
    }
}
