using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Interfaces;

// Defines all database operations for the pipelines table.
// Controllers and services depend on this interface, not on EF Core directly.
// That's what makes unit testing possible — we can swap in a mock.
public interface IPipelineRepository
{
    Task<Pipeline?> FindByIdAsync(int id);
    Task<IEnumerable<Pipeline>> GetAllAsync();
    Task<int> CountAsync();

    // Returns pipelines that have never run OR whose last run was before the cutoff.
    Task<IEnumerable<Pipeline>> GetStalePipelinesAsync(DateTime cutoff);

    // Add stages the new entity; CommitAsync() on IUnitOfWork is what actually saves it.
    void Add(Pipeline pipeline);
}
