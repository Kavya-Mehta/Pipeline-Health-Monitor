namespace PipelineHealthMonitor.Interfaces;

/// <summary>
/// Defines the data operations for RunError entities.
///
/// RunErrors are always created as part of completing a run (via IUnitOfWork),
/// so this interface only needs query methods — the controller never creates
/// errors directly, it does that through PipelineRun.Errors.Add().
/// </summary>
public interface IRunErrorRepository
{
    /// <summary>
    /// Number of CRITICAL-severity errors that occurred after the given cutoff time.
    /// Used by the health endpoint to surface recent critical failures.
    /// </summary>
    Task<int> CountCriticalSinceAsync(DateTime cutoff);
}
