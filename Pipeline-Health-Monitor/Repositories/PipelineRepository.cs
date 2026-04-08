using Microsoft.EntityFrameworkCore;
using PipelineHealthMonitor.Data;
using PipelineHealthMonitor.Interfaces;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Repositories;

public class PipelineRepository : IPipelineRepository
{
    protected readonly AppDbContext _db;

    public PipelineRepository(AppDbContext db) => _db = db;

    public Task<Pipeline?> FindByIdAsync(int id)
        => _db.Pipelines.FindAsync(id).AsTask();

    public Task<List<Pipeline>> GetAllAsync()
        => _db.Pipelines.OrderBy(p => p.Name).ToListAsync();

    public Task<int> CountAsync()
        => _db.Pipelines.CountAsync();

    /// <summary>
    /// Returns pipelines that have had no activity since the cutoff.
    /// !p.Runs.Any() catches pipelines that have never had a run.
    /// p.Runs.Max(r => r.StartedAt) is translated by EF Core to a SQL MAX() subquery.
    /// </summary>
    public Task<List<Pipeline>> GetStalePipelinesAsync(DateTime cutoff)
        => _db.Pipelines
              .Where(p => !p.Runs.Any() || p.Runs.Max(r => r.StartedAt) < cutoff)
              .ToListAsync();

    public void Add(Pipeline pipeline) => _db.Pipelines.Add(pipeline);
}
