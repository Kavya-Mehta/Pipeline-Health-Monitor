using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PipelineHealthMonitor.Data;
using PipelineHealthMonitor.DTOs;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Controllers;

/// <summary>
/// All 5 required endpoints live here.
///
/// [ApiController]    → automatic model validation (returns 400 if request is invalid)
/// [Route("pipelines")] → base path, so endpoints below resolve as /pipelines/...
/// </summary>
[ApiController]
[Route("pipelines")]
public class PipelineRunsController : ControllerBase
{
    private readonly AppDbContext _db;

    // Constructor injection — ASP.NET Core DI hands us the DbContext automatically
    public PipelineRunsController(AppDbContext db)
    {
        _db = db;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // POST /pipelines/runs
    // Start a new pipeline run — creates a record in status RUNNING
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPost("runs")]
    [ProducesResponseType(typeof(PipelineRunResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartRun([FromBody] StartRunRequest req)
    {
        // Verify the pipeline exists before creating a run for it
        var pipeline = await _db.Pipelines.FindAsync(req.PipelineId);
        if (pipeline is null)
            return NotFound(new { error = $"Pipeline with id {req.PipelineId} not found." });

        var run = new PipelineRun
        {
            PipelineId  = req.PipelineId,
            TriggeredBy = req.TriggeredBy,
            StartedAt   = req.StartedAt?.ToUniversalTime() ?? DateTime.UtcNow,
            Status      = "RUNNING",
            RecordsRead     = 0,
            RecordsWritten  = 0,
            RecordsFailed   = 0
        };

        _db.PipelineRuns.Add(run);
        await _db.SaveChangesAsync();

        // Reload with navigation properties so the response includes pipeline info
        await _db.Entry(run).Reference(r => r.Pipeline).LoadAsync();

        return CreatedAtAction(
            nameof(GetRun),
            new { id = run.Id },
            MapToResponse(run));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // PATCH /pipelines/runs/{id}/complete
    // Mark a RUNNING run as finished (SUCCESS / FAILED / PARTIAL) + record stats
    // ──────────────────────────────────────────────────────────────────────────
    [HttpPatch("runs/{id}/complete")]
    [ProducesResponseType(typeof(PipelineRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompleteRun(int id, [FromBody] CompleteRunRequest req)
    {
        // Include(r => r.Errors) eagerly loads related errors in the same DB query
        var run = await _db.PipelineRuns
            .Include(r => r.Pipeline)
            .Include(r => r.Errors)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (run is null)
            return NotFound(new { error = $"Run with id {id} not found." });

        if (run.Status != "RUNNING")
            return BadRequest(new { error = $"Run {id} is already in status '{run.Status}' and cannot be completed again." });

        run.Status         = req.Status;
        run.RecordsRead    = req.RecordsRead;
        run.RecordsWritten = req.RecordsWritten;
        run.RecordsFailed  = req.RecordsFailed;
        run.EndedAt        = req.EndedAt?.ToUniversalTime() ?? DateTime.UtcNow;

        // Attach any errors reported for this run
        foreach (var errInput in req.Errors)
        {
            run.Errors.Add(new RunError
            {
                RunId        = run.Id,
                ErrorCode    = errInput.ErrorCode,
                ErrorMessage = errInput.ErrorMessage,
                Severity     = errInput.Severity,
                OccurredAt   = errInput.OccurredAt?.ToUniversalTime() ?? DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync();
        return Ok(MapToResponse(run));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /pipelines/runs
    // List all runs — supports filtering and pagination via query string
    // Example: GET /pipelines/runs?pipelineId=1&status=FAILED&page=2&pageSize=10
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet("runs")]
    [ProducesResponseType(typeof(PagedRunsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRuns(
        [FromQuery] int?    pipelineId = null,
        [FromQuery] string? status     = null,
        [FromQuery] int     page       = 1,
        [FromQuery] int     pageSize   = 20)
    {
        // Clamp page size to prevent abuse
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        // Build the query — EF Core translates this to SQL WHERE clauses
        var query = _db.PipelineRuns
            .Include(r => r.Pipeline)
            .Include(r => r.Errors)
            .AsQueryable();

        if (pipelineId.HasValue)
            query = query.Where(r => r.PipelineId == pipelineId.Value);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status.ToUpper());

        var totalCount = await query.CountAsync();

        // Skip = go past previous pages, Take = grab this page
        var runs = await query
            .OrderByDescending(r => r.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedRunsResponse
        {
            TotalCount = totalCount,
            Page       = page,
            PageSize   = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            Items      = runs.Select(MapToResponse).ToList()
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /pipelines/runs/{id}
    // Fetch a single run by ID with full error list
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet("runs/{id}")]
    [ProducesResponseType(typeof(PipelineRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRun(int id)
    {
        var run = await _db.PipelineRuns
            .Include(r => r.Pipeline)
            .Include(r => r.Errors)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (run is null)
            return NotFound(new { error = $"Run with id {id} not found." });

        return Ok(MapToResponse(run));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GET /pipelines/health
    // Aggregate health snapshot across all pipelines
    // ──────────────────────────────────────────────────────────────────────────
    [HttpGet("health")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHealth()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        var totalPipelines = await _db.Pipelines.CountAsync();
        var totalRuns      = await _db.PipelineRuns.CountAsync();
        var runningCount   = await _db.PipelineRuns.CountAsync(r => r.Status == "RUNNING");
        var successCount   = await _db.PipelineRuns.CountAsync(r => r.Status == "SUCCESS");
        var failedCount    = await _db.PipelineRuns.CountAsync(r => r.Status == "FAILED");
        var partialCount   = await _db.PipelineRuns.CountAsync(r => r.Status == "PARTIAL");

        var failuresLast24h = await _db.PipelineRuns
            .CountAsync(r => r.Status == "FAILED" && r.StartedAt >= cutoff);

        var criticalLast24h = await _db.RunErrors
            .CountAsync(e => e.Severity == "CRITICAL" && e.OccurredAt >= cutoff);

        // SUM of all records read across completed runs
        var totalProcessed = await _db.PipelineRuns
            .Where(r => r.Status != "RUNNING")
            .SumAsync(r => (long)r.RecordsRead);

        // Determine overall health
        string overallHealth = failuresLast24h > 0 ? "CRITICAL"
                             : partialCount > 0    ? "DEGRADED"
                                                   : "HEALTHY";

        return Ok(new HealthResponse
        {
            TotalPipelines          = totalPipelines,
            TotalRuns               = totalRuns,
            RunningCount            = runningCount,
            SuccessCount            = successCount,
            FailedCount             = failedCount,
            PartialCount            = partialCount,
            FailuresLast24Hours     = failuresLast24h,
            CriticalErrorsLast24Hours = criticalLast24h,
            TotalRecordsProcessed   = totalProcessed,
            OverallHealth           = overallHealth
        });
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Private helper — converts a PipelineRun model into a PipelineRunResponse DTO.
    // Keeping this here avoids repeating the same mapping in every endpoint.
    // ──────────────────────────────────────────────────────────────────────────
    private static PipelineRunResponse MapToResponse(PipelineRun run) => new()
    {
        Id             = run.Id,
        PipelineId     = run.PipelineId,
        PipelineName   = run.Pipeline?.Name ?? string.Empty,
        SourceSystem   = run.Pipeline?.SourceSystem ?? string.Empty,
        SinkSystem     = run.Pipeline?.SinkSystem ?? string.Empty,
        StartedAt      = run.StartedAt,
        EndedAt        = run.EndedAt,
        Status         = run.Status,
        RecordsRead    = run.RecordsRead,
        RecordsWritten = run.RecordsWritten,
        RecordsFailed  = run.RecordsFailed,
        TriggeredBy    = run.TriggeredBy,
        Errors         = run.Errors.Select(e => new ErrorDto
        {
            Id           = e.Id,
            ErrorCode    = e.ErrorCode,
            ErrorMessage = e.ErrorMessage,
            Severity     = e.Severity,
            OccurredAt   = e.OccurredAt
        }).ToList()
    };
}
