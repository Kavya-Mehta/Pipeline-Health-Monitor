namespace PipelineHealthMonitor.Interfaces;

/// <summary>
/// The Unit of Work coordinates multiple repositories under one database transaction.
///
/// Why this matters:
/// Without Unit of Work, each repository would need its own SaveChangesAsync() call.
/// If the pipeline check succeeds but the run insert fails halfway through, you could
/// end up with partial data. With Unit of Work, all changes are batched and committed
/// atomically — either everything succeeds or nothing is written.
///
/// Think of it like a shopping cart:
///   - Repositories are the items you add to the cart
///   - CommitAsync() is the checkout — everything is processed together
///
/// IDisposable:
/// AppDbContext holds database connections and other unmanaged resources.
/// By implementing IDisposable, we ensure those are released after each HTTP request.
/// ASP.NET Core DI calls Dispose() automatically at the end of each request scope.
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>Access all Pipeline operations through this property.</summary>
    IPipelineRepository Pipelines { get; }

    /// <summary>Access all PipelineRun operations through this property.</summary>
    IPipelineRunRepository PipelineRuns { get; }

    /// <summary>Access all RunError operations through this property.</summary>
    IRunErrorRepository RunErrors { get; }

    /// <summary>
    /// Persists all pending changes from ALL repositories to the database in one transaction.
    /// Returns the number of rows affected.
    /// Call this once at the end of an operation, never inside repository methods.
    /// </summary>
    Task<int> CommitAsync();
}
