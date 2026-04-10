using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Interfaces;

public interface IPipelineRunRepository
{
    // Fetches a run and eagerly loads its pipeline + errors in one query.
    Task<PipelineRun?> GetByIdWithDetailsAsync(int id);

    // Paginated list with optional filters.
    Task<IEnumerable<PipelineRun>> GetFilteredAsync(int? pipelineId, string? status, int page, int pageSize);

    // Aggregate counts used by the health endpoint.
    Task<int> CountAsync();
    Task<int> CountByStatusAsync(string status);
    Task<int> CountFailedSinceAsync(DateTime since);
    Task<long> SumRecordsReadForCompletedAsync();

    // Used by the background worker.
    Task<IEnumerable<PipelineRun>> GetStuckRunsAsync(DateTime cutoff);
    Task<IEnumerable<PipelineRun>> GetRecentCompletedRunsAsync(int pipelineId, int count);

    void Add(PipelineRun run);
}
