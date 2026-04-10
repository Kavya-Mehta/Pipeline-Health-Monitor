namespace PipelineHealthMonitor.DTOs;

// ── Inbound DTOs (what callers send in the request body) ────────────────────

public class CreatePipelineRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SourceSystem { get; set; } = string.Empty;
    public string SinkSystem { get; set; } = string.Empty;
}

public class StartRunRequest
{
    public int PipelineId { get; set; }
    public string? TriggeredBy { get; set; }
}

public class CompleteRunRequest
{
    // Must be SUCCESS, FAILED, or PARTIAL
    public string Status { get; set; } = string.Empty;
    public int? RecordsRead { get; set; }
    public int? RecordsWritten { get; set; }
    public int? RecordsFailed { get; set; }
    public List<ErrorRequest> Errors { get; set; } = new();
}

public class ErrorRequest
{
    public string ErrorCode { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
