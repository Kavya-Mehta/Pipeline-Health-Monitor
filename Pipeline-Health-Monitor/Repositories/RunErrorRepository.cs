using Microsoft.EntityFrameworkCore;
using PipelineHealthMonitor.Data;
using PipelineHealthMonitor.Interfaces;

namespace PipelineHealthMonitor.Repositories;

/// <summary>
/// Concrete implementation of IRunErrorRepository.
///
/// RunErrors are never created directly through this repository —
/// they are created by adding to PipelineRun.Errors and then committing.
/// EF Core's change tracker handles the INSERT for each error automatically.
/// This repository only provides query methods needed by the health endpoint.
/// </summary>
public class RunErrorRepository : IRunErrorRepository
{
    protected readonly AppDbContext _db;

    public RunErrorRepository(AppDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc/>
    public Task<int> CountCriticalSinceAsync(DateTime cutoff)
        => _db.RunErrors.CountAsync(e => e.Severity == "CRITICAL" && e.OccurredAt >= cutoff);
}
