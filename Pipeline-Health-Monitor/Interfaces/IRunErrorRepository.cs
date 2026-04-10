using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Interfaces;

public interface IRunErrorRepository
{
    Task<int> CountCriticalSinceAsync(DateTime since);
    void Add(RunError error);
}
