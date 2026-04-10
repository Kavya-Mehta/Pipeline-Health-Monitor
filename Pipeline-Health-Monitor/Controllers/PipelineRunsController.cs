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

    // POST /pipelines/runs — opens a new run with status RUNNING
    [HttpPost("runs")]
    public async Task<IActionResult> StartRun([FromBody] StartRunRequest request)
    {
        var pipeline = await _uow.Pipelines.FindByIdAsync(request.PipelineId);
        if (pipeline is null)
            return NotFound(new { message = $"Pipeline {request.PipelineId} not found." });

        var run = new PipelineRun
        {
            PipelineId  = request.PipelineId,
            Status      = "RUNNING",
            TriggeredBy = request.TriggeredBy,
            StartedAt   = DateTime.UtcNow
        };

        _uow.PipelineRuns.Add(run);
        await _uow.CommitAsync();

        var response = MapToResponse(run, pipeline.Name);
        return CreatedAtAction(nameof(GetRun), new { id = run.Id }, response);
    }

    // PATCH /pipelines/runs/{id}/complete — closes a run with final counts and status
    [HttpPatch("runs/{id:int}/complete")]
    public async Task<IActionResult> CompleteRun(int id, [FromBody] CompleteRunRequest request)
    {
        var run = await _uow.PipelineRuns.GetByIdWithDetailsAsync(id);
        if (run is null)
            return NotFound(new { message = $"Run {id} not found." });

        // Can only complete a run that is currently RUNNING
        if (run.Status != "RUNNING")
            return BadRequest(new { message = $"Run {id} is already in status {run.Status}." });

        run.Status         = request.Status.ToUpper();
        run.EndedAt        = DateTime.UtcNow;
        run.RecordsRead    = request.RecordsRead;
        run.RecordsWritten = request.RecordsWritten;
        run.RecordsFailed  = request.RecordsFailed;

        foreach (var err in request.Errors)
        {
            _uow.RunErrors.Add(new RunError
            {
                RunId       = run.Id,
                ErrorCode   = err.ErrorCode,
                Message     = err.Message,
                OccurredAt  = DateTime.UtcNow
            });
        }

        await _uow.CommitAsync();
        return Ok(MapToResponse(run, run.Pipeline.Name));
    }

    // GET /pipelines/runs — list with optional filters and pagination
    [HttpGet("runs")]
    public async Task<IActionResult> GetRuns(
        [FromQuery] int? pipelineId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Min(pageSize, 100); // hard cap
        var runs = await _uow.PipelineRuns.GetFilteredAsync(pipelineId, status, page, pageSize);
        var responses = runs.Select(r => MapToResponse(r, r.Pipeline.Name));
        return Ok(responses);
    }

    // GET /pipelines/runs/{id} — full run detail including errors
    [HttpGet("runs/{id:int}")]
    public async Task<IActionResult> GetRun(int id)
    {
        var run = await _uow.PipelineRuns.GetByIdWithDetailsAsync(id);
        if (run is null)
            return NotFound(new { message = $"Run {id} not found." });

        return Ok(MapToResponse(run, run.Pipeline.Name));
    }

    // GET /pipelines/health — aggregated snapshot of the entire system
    [HttpGet("health")]
    public async Task<IActionResult> GetHealth()
    {
        var since24h = DateTime.UtcNow.AddHours(-24);

        var totalPipelines     = await _uow.Pipelines.CountAsync();
        var totalRuns          = await _uow.PipelineRuns.CountAsync();
        var runningRuns        = await _uow.PipelineRuns.CountByStatusAsync("RUNNING");
        var successRuns        = await _uow.PipelineRuns.CountByStatusAsync("SUCCESS");
        var failedRuns         = await _uow.PipelineRuns.CountByStatusAsync("FAILED");
        var partialRuns        = await _uow.PipelineRuns.CountByStatusAsync("PARTIAL");
        var failuresLast24h    = await _uow.PipelineRuns.CountFailedSinceAsync(since24h);
        var totalRecords       = await _uow.PipelineRuns.SumRecordsReadForCompletedAsync();
        var criticalErrors     = await _uow.RunErrors.CountCriticalSinceAsync(since24h);

        // Determine overall health based on the last 24 hours.
        string overallHealth;
        if (failuresLast24h > 0)
            overallHealth = "CRITICAL";
        else if (partialRuns > 0)
            overallHealth = "DEGRADED";
        else
            overallHealth = "HEALTHY";

        return Ok(new HealthResponse
        {
            OverallHealth            = overallHealth,
            TotalPipelines           = totalPipelines,
            TotalRuns                = totalRuns,
            RunningRuns              = runningRuns,
            SuccessRuns              = successRuns,
            FailedRuns               = failedRuns,
            PartialRuns              = partialRuns,
            FailuresLast24Hours      = failuresLast24h,
            TotalRecordsProcessed    = totalRecords,
            CriticalErrorsLast24Hours = criticalErrors
        });
    }

    private static PipelineRunResponse MapToResponse(PipelineRun r, string pipelineName) => new()
    {
        Id             = r.Id,
        PipelineId     = r.PipelineId,
        PipelineName   = pipelineName,
        Status         = r.Status,
        TriggeredBy    = r.TriggeredBy,
        StartedAt      = r.StartedAt,
        EndedAt        = r.EndedAt,
        RecordsRead    = r.RecordsRead,
        RecordsWritten = r.RecordsWritten,
        RecordsFailed  = r.RecordsFailed,
        ErrorRate      = r.ErrorRate,
        Errors         = r.Errors.Select(e => new RunErrorResponse
        {
            Id          = e.Id,
            ErrorCode   = e.ErrorCode,
            Message     = e.Message,
            OccurredAt  = e.OccurredAt
        }).ToList()
    };
}
