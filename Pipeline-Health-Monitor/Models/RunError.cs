namespace PipelineHealthMonitor.Models;

// An error record attached to a specific pipeline run.
// Stored as separate rows (not JSON) so they can be indexed and queried.
public class RunError
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public string ErrorCode { get; set; } = string.Empty;   // e.g. RUN_TIMEOUT, VALIDATION_ERROR
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }

    // Navigation property back to the run
    public PipelineRun Run { get; set; } = null!;
}
