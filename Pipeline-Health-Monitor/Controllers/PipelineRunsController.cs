using Microsoft.AspNetCore.Mvc;
using PipelineHealthMonitor.DTOs;
using PipelineHealthMonitor.Interfaces;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Controllers;

[ApiController]
[Route("pipelines")]
public class PipelineRunsController : ControllerBase
{
    private readonly IUnitOfWork _uow;

    public PipelineRunsController(IUnitOfWork uow)
    {
        _uow = uow;
    }

    [HttpPost("runs")]
    [ProducesResponseType(typeof(PipelineRunResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartRun([FromBody] StartRunRequest req)
    {
        var pipeline = await _uow.Pipelines.FindByIdAsync(req.PipelineId);
        if (pipeline is null)
            return NotFound(new { error = $"Pipeline with id {req.PipelineId} not found." });

        var run = new PipelineRun
        {
            PipelineId     = req.PipelineId,
            TriggeredBy    = req.TriggeredBy,
            StartedAt      = req.StartedAt?.ToUniversalTime() ?? DateTime.UtcNow,
            Status         = "RUNNING",
            RecordsRead    = 0,
            RecordsWritten = 0,
            RecordsFailed  = 0
        };

        _uow.PipelineRuns.Add(run);
        await _uow.CommitAsync();
        run.Pipeline = pipeline;

        return CreatedAtAction(nameof(GetRun), new { id = run.Id }, MapToResponse(run));
    }

    [HttpPatch("runs/{id}/complete")]
    [ProducesResponseType(typeof(PipelineRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompleteRun(int id, [FromBody] CompleteRunRequest req)
    {
        var run = await _uow.PipelineRuns.GetByIdWithDetailsAsync(id);
        if (run is null)
            return NotFound(new { error = $"Run with id {id} not found." });

        if (run.Status != "RUNNING")
            return BadRequest(new { error = $"Run {id} is already in status {run.Status} and cannot be completed again." });

        run.Status         = req.Status;
        run.RecordsRead    = req.RecordsRead;
        run.RecordsWritten = req.RecordsWritten;
        run.RecordsFailed  = req.RecordsFailed;
        run.EndedAt        = req.EndedAt?.ToUniversalTime() ?? DateTime.UtcNow;

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

        await _uow.CommitAsync();
        return Ok(MapToResponse(run));
    }

    [HttpGet("runs")]
    [ProducesResponseType(typeof(PagedRunsResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRuns(
        [FromQuery] int?    pipelineId = null,
        [FromQuery] string? status     = null,
        [FromQuery] int     page       = 1,
        [FromQuery] int     pageSize   = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        var (items, totalCount) = await _uow.PipelineRuns.GetPagedAsync(pipelineId, status, page, pageSize);

        return Ok(new PagedRunsResponse
        {
            TotalCount = totalCount,
            Page       = page,
            PageSize   = pageSize,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            Items      = items.Select(MapToResponse).ToList()
        });
    }

    [HttpGet("runs/{id}")]
    [ProducesResponseType(typeof(PipelineRunResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRun(int id)
    {
        var run = await _uow.PipelineRuns.GetByIdWithDetailsAsync(id);
        if (run is null)
            return NotFound(new { error = $"Run with id {id} not found." });

        return Ok(MapToResponse(run));
    }

    [HttpGet("health")]
    [ProducesResponseType(typeof(HealthResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHealth()
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        var totalPipelines  = await _uow.Pipelines.CountAsync();
        var totalRuns       = await _uow.PipelineRuns.CountAsync();
        var runningCount    = await _uow.PipelineRuns.CountByStatusAsync("RUNNING");
        var successCount    = await _uow.PipelineRuns.CountByStatusAsync("SUCCESS");
        var failedCount     = await _uow.PipelineRuns.CountByStatusAsync("FAILED");
        var partialCount    = await _uow.PipelineRuns.CountByStatusAsync("PARTIAL");
        var failuresLast24h = await _uow.PipelineRuns.CountFailedSinceAsync(cutoff);
        var criticalLast24h = await _uow.RunErrors.CountCriticalSinceAsync(cutoff);
        var totalProcessed  = await _uow.PipelineRuns.SumRecordsReadForCompletedAsync();

        string overallHealth = failuresLast24h > 0 ? "CRITICAL"
                             : partialCount    > 0 ? "DEGRADED"
                                                   : "HEALTHY";

        return Ok(new HealthResponse
        {
            TotalPipelines            = totalPipelines,
            TotalRuns                 = totalRuns,
            RunningCount              = runningCount,
            SuccessCount              = successCount,
            FailedCount               = failedCount,
            PartialCount              = partialCount,
            FailuresLast24Hours       = failuresLast24h,
            CriticalErrorsLast24Hours = criticalLast24h,
            TotalRecordsProcessed     = totalProcessed,
            OverallHealth             = overallHealth
        });
    }

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
