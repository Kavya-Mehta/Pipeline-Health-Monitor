using Microsoft.EntityFrameworkCore;
using PipelineHealthMonitor.Data;
using PipelineHealthMonitor.Interfaces;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Repositories;

// Concrete EF Core implementation of IPipelineRepository.
// All LINQ queries live here — controllers never touch DbContext directly.
public class PipelineRepository : IPipelineRepository
{
    private readonly AppDbContext _context;

    public PipelineRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Pipeline?> FindByIdAsync(int id) =>
        await _context.Pipelines.FindAsync(id);

    public async Task<IEnumerable<Pipeline>> GetAllAsync() =>
        await _context.Pipelines
            .OrderBy(p => p.Name)
            .ToListAsync();

    public async Task<int> CountAsync() =>
        await _context.Pipelines.CountAsync();

    // A pipeline is stale if it has never had a run, OR its most recent run
    // started before the cutoff time.
    public async Task<IEnumerable<Pipeline>> GetStalePipelinesAsync(DateTime cutoff) =>
        await _context.Pipelines
            .Include(p => p.Runs)
            .Where(p => !p.Runs.Any() || p.Runs.Max(r => r.StartedAt) < cutoff)
            .ToListAsync();

    // Add just stages the entity in EF Core's change tracker.
    // Nothing is written to the DB until UnitOfWork.CommitAsync() is called.
    public void Add(Pipeline pipeline) =>
        _context.Pipelines.Add(pipeline);
}
