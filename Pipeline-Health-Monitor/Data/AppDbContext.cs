using Microsoft.EntityFrameworkCore;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Data;

/// <summary>
/// AppDbContext is EF Core's "unit of work" — it represents a session with the database.
/// Every table in your DB corresponds to a DbSet property here.
/// OnModelCreating() is where we configure table names, column names, constraints, and indexes.
/// </summary>
public class AppDbContext : DbContext
{
    // Constructor receives options (connection string etc.) injected by ASP.NET Core DI
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Pipeline> Pipelines => Set<Pipeline>();
    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();
    public DbSet<RunError> RunErrors => Set<RunError>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // ── pipelines table ──────────────────────────────────────────────────
        modelBuilder.Entity<Pipeline>(entity =>
        {
            entity.ToTable("pipelines");
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).HasColumnName("id");
            entity.Property(p => p.Name)
                  .HasColumnName("name").IsRequired().HasMaxLength(200);
            entity.Property(p => p.SourceSystem)
                  .HasColumnName("source_system").IsRequired().HasMaxLength(100);
            entity.Property(p => p.SinkSystem)
                  .HasColumnName("sink_system").IsRequired().HasMaxLength(100);
            entity.Property(p => p.Description)
                  .HasColumnName("description").HasMaxLength(500);
            entity.Property(p => p.CreatedAt)
                  .HasColumnName("created_at")
                  .HasDefaultValueSql("GETUTCDATE()");
        });

        // ── pipeline_runs table ───────────────────────────────────────────────
        modelBuilder.Entity<PipelineRun>(entity =>
        {
            entity.ToTable("pipeline_runs", t =>
                t.HasCheckConstraint(
                    "CK_pipeline_runs_status",
                    "status IN ('RUNNING', 'SUCCESS', 'FAILED', 'PARTIAL')"));
            entity.HasKey(r => r.Id);
            entity.Property(r => r.Id).HasColumnName("id");
            entity.Property(r => r.PipelineId).HasColumnName("pipeline_id");
            entity.Property(r => r.StartedAt).HasColumnName("started_at");
            entity.Property(r => r.EndedAt).HasColumnName("ended_at");
            entity.Property(r => r.Status)
                  .HasColumnName("status").IsRequired().HasMaxLength(20);
            entity.Property(r => r.RecordsRead).HasColumnName("records_read");
            entity.Property(r => r.RecordsWritten).HasColumnName("records_written");
            entity.Property(r => r.RecordsFailed).HasColumnName("records_failed");
            entity.Property(r => r.TriggeredBy)
                  .HasColumnName("triggered_by").IsRequired().HasMaxLength(100);

            // Index speeds up queries filtering by pipeline_id
            entity.HasIndex(r => r.PipelineId)
                  .HasDatabaseName("IX_pipeline_runs_pipeline_id");

            // Foreign key relationship: many runs belong to one pipeline
            entity.HasOne(r => r.Pipeline)
                  .WithMany(p => p.Runs)
                  .HasForeignKey(r => r.PipelineId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── run_errors table ─────────────────────────────────────────────────
        modelBuilder.Entity<RunError>(entity =>
        {
            entity.ToTable("run_errors", t =>
                t.HasCheckConstraint(
                    "CK_run_errors_severity",
                    "severity IN ('INFO', 'WARNING', 'CRITICAL')"));
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.RunId).HasColumnName("run_id");
            entity.Property(e => e.ErrorCode)
                  .HasColumnName("error_code").IsRequired().HasMaxLength(50);
            entity.Property(e => e.ErrorMessage)
                  .HasColumnName("error_message").IsRequired().HasMaxLength(1000);
            entity.Property(e => e.Severity)
                  .HasColumnName("severity").IsRequired().HasMaxLength(20);
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at");

            // Index speeds up queries filtering by run_id
            entity.HasIndex(e => e.RunId)
                  .HasDatabaseName("IX_run_errors_run_id");

            // Foreign key: many errors belong to one run
            entity.HasOne(e => e.Run)
                  .WithMany(r => r.Errors)
                  .HasForeignKey(e => e.RunId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
