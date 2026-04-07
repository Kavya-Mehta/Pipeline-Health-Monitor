using System.ComponentModel.DataAnnotations;

namespace PipelineHealthMonitor.DTOs;

// ──────────────────────────────────────────────────────────────────────────────
// REQUEST DTOs — shapes of JSON that clients send TO the API
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>POST /pipelines — create a named pipeline definition</summary>
public class CreatePipelineRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string SourceSystem { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string SinkSystem { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }
}

/// <summary>POST /pipelines/runs — start a new pipeline run</summary>
public class StartRunRequest
{
    [Required]
    public int PipelineId { get; set; }

    [Required, MaxLength(100)]
    public string TriggeredBy { get; set; } = string.Empty;

    /// <summary>Defaults to UTC now if omitted</summary>
    public DateTime? StartedAt { get; set; }
}

/// <summary>One error entry within a CompleteRunRequest</summary>
public class ErrorInput
{
    [Required, MaxLength(50)]
    public string ErrorCode { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Must be INFO, WARNING, or CRITICAL</summary>
    [Required, RegularExpression("^(INFO|WARNING|CRITICAL)$",
        ErrorMessage = "Severity must be INFO, WARNING, or CRITICAL")]
    public string Severity { get; set; } = "INFO";

    /// <summary>Defaults to UTC now if omitted</summary>
    public DateTime? OccurredAt { get; set; }
}

/// <summary>PATCH /pipelines/runs/{id}/complete — finish a pipeline run</summary>
public class CompleteRunRequest
{
    /// <summary>Must be SUCCESS, FAILED, or PARTIAL</summary>
    [Required, RegularExpression("^(SUCCESS|FAILED|PARTIAL)$",
        ErrorMessage = "Status must be SUCCESS, FAILED, or PARTIAL")]
    public string Status { get; set; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int RecordsRead { get; set; }

    [Range(0, int.MaxValue)]
    public int RecordsWritten { get; set; }

    [Range(0, int.MaxValue)]
    public int RecordsFailed { get; set; }

    /// <summary>Defaults to UTC now if omitted</summary>
    public DateTime? EndedAt { get; set; }

    /// <summary>Optional list of errors encountered during this run</summary>
    public List<ErrorInput> Errors { get; set; } = new();
}

// ──────────────────────────────────────────────────────────────────────────────
// RESPONSE DTOs — shapes of JSON the API sends BACK to clients
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>A single error in a run response</summary>
public class ErrorDto
{
    public int Id { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}

/// <summary>Full pipeline run with embedded pipeline info and errors</summary>
public class PipelineRunResponse
{
    public int Id { get; set; }
    public int PipelineId { get; set; }
    public string PipelineName { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string SinkSystem { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string Status { get; set; } = string.Empty;
    public int RecordsRead { get; set; }
    public int RecordsWritten { get; set; }
    public int RecordsFailed { get; set; }
    public string TriggeredBy { get; set; } = string.Empty;
    public List<ErrorDto> Errors { get; set; } = new();
}

/// <summary>Paginated list response wrapper for GET /pipelines/runs</summary>
public class PagedRunsResponse
{
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<PipelineRunResponse> Items { get; set; } = new();
}

/// <summary>
/// GET /pipelines/health — aggregate health snapshot.
/// OverallHealth is calculated:
///   CRITICAL  → any FAILED run in the last 24h
///   DEGRADED  → any PARTIAL run in the last 24h (but no FAILED)
///   HEALTHY   → no failures or partials in the last 24h
/// </summary>
public class HealthResponse
{
    public int TotalPipelines { get; set; }
    public int TotalRuns { get; set; }
    public int RunningCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public int PartialCount { get; set; }
    public int FailuresLast24Hours { get; set; }
    public int CriticalErrorsLast24Hours { get; set; }
    public long TotalRecordsProcessed { get; set; }
    public string OverallHealth { get; set; } = string.Empty;
}

/// <summary>Simple pipeline info response</summary>
public class PipelineResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string SinkSystem { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}
