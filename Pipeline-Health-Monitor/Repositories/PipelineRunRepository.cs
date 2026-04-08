using Microsoft.EntityFrameworkCore;
using PipelineHealthMonitor.Data;
using PipelineHealthMonitor.Interfaces;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Repositories;

public class PipelineRunRepository : IPipelineRunRepository
{
    protected readonly AppDbContext _db;

    public PipelineRunRepository(AppDbContext db) => _db = db;

    public Task<PipelineRun?> GetByIdWithDetailsAsync(int id)
        => _db.PipelineRuns
              .Include(r => r.Pipeline)
              .Include(r => r.Errors)
              .FirstOrDefaultAsync(r => r.Id == id);

    public async Task<(List<PipelineRun> Items, int TotalCount)> GetPagedAsync(
        int? pipelineId, string? status, int page, int pageSize)
    {
        var query = _db.PipelineRuns
            .Include(r => r.Pipeline)
            .Include(r => r.Errors)
            .AsQueryable();

        if (pipelineId.HasValue)
            query = query.Where(r => r.PipelineId == pipelineId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status.ToUpper());

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public Task<int> CountAsync() => _db.PipelineRuns.CountAsync();

    public Task<int> CountByStatusAsync(string status)
        => _db.PipelineRuns.CountAsync(r => r.Status == status);

    public Task<int> CountFailedSinceAsync(DateTime cutoff)
        => _db.PipelineRuns.CountAsync(r => r.Status == "FAILED" && r.StartedAt >= cutoff);

    public async Task<long> SumRecordsReadForCompletedAsync()
        => await _db.PipelineRuns
                    .Where(r => r.Status != "RUNNING")
                    .SumAsync(r => (long)r.RecordsRead);

    /// <summary>
    /// Returns all RUNNING runs whose StartedAt is older than startedBefore.
    /// Includes Pipeline so the worker can log the pipeline name.
    /// </summary>
    public Task<List<PipelineRun>> GetStuckRunsAsync(DateTime startedBefore)
        => _db.PipelineRuns
              .Include(r => r.Pipeline)
              .Where(r => r.Status == "RUNNING" && r.StartedAt < startedBefore)
              .ToListAsync();

    /// <summary>
    /// Returns the last N completed runs for a specific pipeline, newest first.
    /// Status != RUNNING excludes in-progress runs from the baseline calculation.
    /// </summary>
    public Task<List<PipelineRun>> GetRecentCompletedRunsAsync(int pipelineId, int count)
        => _db.PipelineRuns
              .Where(r => r.PipelineId == pipelineId && r.Status != "RUNNING")
              .OrderByDescending(r => r.StartedAt)
              .Take(count)
              .ToListAsync();

    public void Add(PipelineRun run) => _db.PipelineRuns.Add(run);
}
