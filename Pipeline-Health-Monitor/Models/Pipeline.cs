namespace PipelineHealthMonitor.Models;

// Represents a named data pipeline definition.
// A pipeline can have many runs over its lifetime.
public class Pipeline
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SourceSystem { get; set; } = string.Empty;
    public string SinkSystem { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // Navigation property: one pipeline -> many runs
    public ICollection<PipelineRun> Runs { get; set; } = new List<PipelineRun>();
}
