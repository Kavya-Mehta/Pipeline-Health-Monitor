namespace PipelineHealthMonitor.Models;

/// <summary>
/// A single execution of a Pipeline.
/// Status is constrained by a SQL CHECK constraint to: RUNNING | SUCCESS | FAILED | PARTIAL
/// </summary>
public class PipelineRun
{
    public int Id { get; set; }
    public int PipelineId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }       // null while still RUNNING
    public string Status { get; set; } = string.Empty;
    public int RecordsRead { get; set; }
    public int RecordsWritten { get; set; }
    public int RecordsFailed { get; set; }
    public string TriggeredBy { get; set; } = string.Empty;

    // Navigation properties — EF Core uses these for JOINs
    public Pipeline Pipeline { get; set; } = null!;
    public ICollection<RunError> Errors { get; set; } = new List<RunError>();
}
