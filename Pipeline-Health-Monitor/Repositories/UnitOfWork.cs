using PipelineHealthMonitor.Data;
using PipelineHealthMonitor.Interfaces;

namespace PipelineHealthMonitor.Repositories;

/// <summary>
/// Concrete implementation of IUnitOfWork.
///
/// This class owns one AppDbContext instance and creates one of each repository,
/// passing the SAME context to all of them. This is the critical design point:
/// because all three repositories share one context, EF Core tracks all changes
/// together, and a single SaveChangesAsync() call commits everything atomically.
///
/// Lifecycle:
///   1. ASP.NET Core DI creates UnitOfWork (and with it, AppDbContext) per request
///   2. Controller calls repository methods — changes are tracked in memory
///   3. Controller calls CommitAsync() — one SQL transaction writes everything
///   4. Request ends, DI calls Dispose() — context is released
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    // Lazy initialization — repositories are only created when first accessed.
    // The ? means nullable; we check for null in the property getter and create if needed.
    private IPipelineRepository? _pipelines;
    private IPipelineRunRepository? _pipelineRuns;
    private IRunErrorRepository? _runErrors;

    // AppDbContext is injected by ASP.NET Core DI — we don't call new AppDbContext() ourselves
    public UnitOfWork(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// The ?? operator means: "if _pipelines is null, create a new instance and assign it."
    /// On subsequent accesses, _pipelines already has a value so we return it directly.
    /// All three properties share the same _db instance.
    /// </summary>
    public IPipelineRepository Pipelines
        => _pipelines ??= new PipelineRepository(_db);

    public IPipelineRunRepository PipelineRuns
        => _pipelineRuns ??= new PipelineRunRepository(_db);

    public IRunErrorRepository RunErrors
        => _runErrors ??= new RunErrorRepository(_db);

    /// <summary>
    /// Calls EF Core's SaveChangesAsync() — flushes all pending changes to the database.
    /// This is the single commit point for the entire operation.
    /// Returns the number of rows affected (useful for logging/debugging).
    /// </summary>
    public Task<int> CommitAsync()
        => _db.SaveChangesAsync();

    /// <summary>
    /// Releases the AppDbContext (which closes the database connection).
    /// Called automatically by DI at the end of each HTTP request scope.
    /// </summary>
    public void Dispose()
        => _db.Dispose();
}
