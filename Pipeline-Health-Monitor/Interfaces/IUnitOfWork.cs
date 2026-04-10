namespace PipelineHealthMonitor.Interfaces;

// Unit of Work coordinates all repositories under one shared DbContext.
// CommitAsync() is the single point where all pending changes are written to the DB
// in one transaction. Controllers call CommitAsync() once at the end of a request.
public interface IUnitOfWork : IDisposable
{
    IPipelineRepository Pipelines { get; }
    IPipelineRunRepository PipelineRuns { get; }
    IRunErrorRepository RunErrors { get; }

    Task<int> CommitAsync();
}
