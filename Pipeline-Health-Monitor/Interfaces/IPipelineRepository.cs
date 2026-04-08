using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Interfaces;

public interface IPipelineRepository
{
    Task<Pipeline?> FindByIdAsync(int id);
    Task<List<Pipeline>> GetAllAsync();
    Task<int> CountAsync();

    // Used by the background worker to find pipelines with no recent activity.
    // Returns pipelines whose most recent run started before the cutoff,
    // or pipelines that have never had any runs at all.
    Task<List<Pipeline>> GetStalePipelinesAsync(DateTime cutoff);

    void Add(Pipeline pipeline);
}
