namespace PipelineHealthMonitor.Models;

// One execution of a pipeline. Status moves from RUNNING -> SUCCESS / FAILED / PARTIAL.
public class PipelineRun
{
    public int Id { get; set; }
    public int PipelineId { get; set; }
    public string Status { get; set; } = string.Empty;   // RUNNING | SUCCESS | FAILED | PARTIAL
    public string? TriggeredBy { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }                // null while still RUNNING
    public int? RecordsRead { get; set; }
    public int? RecordsWritten { get; set; }
    public int? RecordsFailed { get; set; }

    // Navigation properties
    public Pipeline Pipeline { get; set; } = null!;
    public ICollection<RunError> Errors { get; set; } = new List<RunError>();

    // Computed in C#, never stored — storing it would risk drift if counts are updated.
    public double? ErrorRate =>
        RecordsRead.HasValue && RecordsRead.Value > 0 && RecordsFailed.HasValue
            ? (double)RecordsFailed.Value / RecordsRead.Value
            : null;
}
