namespace PipelineHealthMonitor.DTOs;

// ── Outbound DTOs (what the API returns in response bodies) ─────────────────

public class PipelineResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SourceSystem { get; set; } = string.Empty;
    public string SinkSystem { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int RunCount { get; set; }
}

public class PipelineRunResponse
{
    public int Id { get; set; }
    public int PipelineId { get; set; }
    public string PipelineName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? TriggeredBy { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? RecordsRead { get; set; }
    public int? RecordsWritten { get; set; }
    public int? RecordsFailed { get; set; }
    public double? ErrorRate { get; set; }
    public List<RunErrorResponse> Errors { get; set; } = new();
}

public class RunErrorResponse
{
    public int Id { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
}

public class HealthResponse
{
    public string OverallHealth { get; set; } = string.Empty;   // HEALTHY | DEGRADED | CRITICAL
    public int TotalPipelines { get; set; }
    public int TotalRuns { get; set; }
    public int RunningRuns { get; set; }
    public int SuccessRuns { get; set; }
    public int FailedRuns { get; set; }
    public int PartialRuns { get; set; }
    public int FailuresLast24Hours { get; set; }
    public long TotalRecordsProcessed { get; set; }
    public int CriticalErrorsLast24Hours { get; set; }
}
