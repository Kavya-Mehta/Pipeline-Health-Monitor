using Microsoft.EntityFrameworkCore;
using PipelineHealthMonitor.Data;
using PipelineHealthMonitor.Interfaces;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Repositories;

public class PipelineRunRepository : IPipelineRunRepository
{
    private readonly AppDbContext _context;

    public PipelineRunRepository(AppDbContext context)
    {
        _context = context;
    }

    // Include() eagerly loads the related Pipeline and Errors in the same SQL query
    // instead of making separate round-trips to the database.
    public async Task<PipelineRun?> GetByIdWithDetailsAsync(int id) =>
        await _context.PipelineRuns
            .Include(r => r.Pipeline)
            .Include(r => r.Errors)
            .FirstOrDefaultAsync(r => r.Id == id);

    public async Task<IEnumerable<PipelineRun>> GetFilteredAsync(
        int? pipelineId, string? status, int page, int pageSize)
    {
        var query = _context.PipelineRuns.Include(r => r.Pipeline).AsQueryable();

        if (pipelineId.HasValue)
            query = query.Where(r => r.PipelineId == pipelineId.Value);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(r => r.Status == status.ToUpper());

        return await query
            .OrderByDescending(r => r.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> CountAsync() =>
        await _context.PipelineRuns.CountAsync();

    public async Task<int> CountByStatusAsync(string status) =>
        await _context.PipelineRuns.CountAsync(r => r.Status == status);

    public async Task<int> CountFailedSinceAsync(DateTime since) =>
        await _context.PipelineRuns
            .CountAsync(r => r.Status == "FAILED" && r.StartedAt >= since);

    public async Task<long> SumRecordsReadForCompletedAsync() =>
        await _context.PipelineRuns
            .Where(r => r.Status != "RUNNING" && r.RecordsRead.HasValue)
            .SumAsync(r => (long)r.RecordsRead!.Value);

    // Returns RUNNING runs that started before the cutoff — they are stuck.
    public async Task<IEnumerable<PipelineRun>> GetStuckRunsAsync(DateTime cutoff) =>
        await _context.PipelineRuns
            .Where(r => r.Status == "RUNNING" && r.StartedAt < cutoff)
            .ToListAsync();

    // Returns the N most recent completed runs for a given pipeline.
    public async Task<IEnumerable<PipelineRun>> GetRecentCompletedRunsAsync(int pipelineId, int count) =>
        await _context.PipelineRuns
            .Where(r => r.PipelineId == pipelineId && r.Status != "RUNNING")
            .OrderByDescending(r => r.StartedAt)
            .Take(count)
            .ToListAsync();

    public void Add(PipelineRun run) =>
        _context.PipelineRuns.Add(run);
}
