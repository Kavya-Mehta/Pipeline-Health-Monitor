using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Interfaces;

public interface IPipelineRunRepository
{
    Task<PipelineRun?> GetByIdWithDetailsAsync(int id);

    Task<(List<PipelineRun> Items, int TotalCount)> GetPagedAsync(
        int? pipelineId, string? status, int page, int pageSize);

    Task<int> CountAsync();
    Task<int> CountByStatusAsync(string status);
    Task<int> CountFailedSinceAsync(DateTime cutoff);
    Task<long> SumRecordsReadForCompletedAsync();

    // Returns RUNNING runs that started before the given time.
    // Used by the worker to detect runs that never completed (stuck runs).
    Task<List<PipelineRun>> GetStuckRunsAsync(DateTime startedBefore);

    // Returns the most recent N completed (non-RUNNING) runs for a pipeline,
    // newest first. Used by the worker to compute a baseline for anomaly detection.
    Task<List<PipelineRun>> GetRecentCompletedRunsAsync(int pipelineId, int count);

    void Add(PipelineRun run);
}
