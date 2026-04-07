namespace PipelineHealthMonitor.Models;

/// <summary>
/// An error that occurred during a PipelineRun.
/// Severity is constrained by a SQL CHECK constraint to: INFO | WARNING | CRITICAL
/// </summary>
public class RunError
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }

    // Navigation property
    public PipelineRun Run { get; set; } = null!;
}
