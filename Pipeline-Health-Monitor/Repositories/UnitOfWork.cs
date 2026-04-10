using PipelineHealthMonitor.Data;
using PipelineHealthMonitor.Interfaces;

namespace PipelineHealthMonitor.Repositories;

// UnitOfWork holds one AppDbContext and lazily initialises each repository.
// All repositories share the SAME context, so all their changes are part of
// the same transaction when CommitAsync() is called.
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _context;

    // Repositories are created on first access (??= operator).
    private IPipelineRepository? _pipelines;
    private IPipelineRunRepository? _pipelineRuns;
    private IRunErrorRepository? _runErrors;

    public UnitOfWork(AppDbContext context)
    {
        _context = context;
    }

    public IPipelineRepository Pipelines =>
        _pipelines ??= new PipelineRepository(_context);

    public IPipelineRunRepository PipelineRuns =>
        _pipelineRuns ??= new PipelineRunRepository(_context);

    public IRunErrorRepository RunErrors =>
        _runErrors ??= new RunErrorRepository(_context);

    // Flushes all pending EF Core changes to the database in one transaction.
    public Task<int> CommitAsync() =>
        _context.SaveChangesAsync();

    public void Dispose() =>
        _context.Dispose();
}
