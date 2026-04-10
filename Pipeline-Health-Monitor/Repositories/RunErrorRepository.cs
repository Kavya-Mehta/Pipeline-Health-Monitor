using Microsoft.EntityFrameworkCore;
using PipelineHealthMonitor.Data;
using PipelineHealthMonitor.Interfaces;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Repositories;

public class RunErrorRepository : IRunErrorRepository
{
    private readonly AppDbContext _context;

    public RunErrorRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<int> CountCriticalSinceAsync(DateTime since) =>
        await _context.RunErrors
            .CountAsync(e => e.OccurredAt >= since);

    public void Add(RunError error) =>
        _context.RunErrors.Add(error);
}
