namespace PipelineHealthMonitor.Models;

/// <summary>
/// Represents a named data pipeline (e.g. "Orders → Warehouse").
/// A Pipeline is a definition; actual executions are PipelineRuns.
/// </summary>
public class Pipeline
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string SinkSystem { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }

    // Navigation property — EF Core uses this to JOIN pipeline_runs
    public ICollection<PipelineRun> Runs { get; set; } = new List<PipelineRun>();
}
