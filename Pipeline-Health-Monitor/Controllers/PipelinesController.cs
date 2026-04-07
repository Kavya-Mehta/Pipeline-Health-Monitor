using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PipelineHealthMonitor.Data;
using PipelineHealthMonitor.DTOs;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Controllers;

/// <summary>
/// Manages Pipeline definitions (the "what" — not the "runs").
/// These are not in the original 5 required endpoints but are needed
/// to create pipelines before you can start runs against them.
/// </summary>
[ApiController]
[Route("pipelines")]
public class PipelinesController : ControllerBase
{
    private readonly AppDbContext _db;

    public PipelinesController(AppDbContext db)
    {
        _db = db;
    }

    // POST /pipelines — create a new pipeline definition
    [HttpPost]
    [ProducesResponseType(typeof(PipelineResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePipeline([FromBody] CreatePipelineRequest req)
    {
        var pipeline = new Pipeline
        {
            Name         = req.Name,
            SourceSystem = req.SourceSystem,
            SinkSystem   = req.SinkSystem,
            Description  = req.Description,
            CreatedAt    = DateTime.UtcNow
        };

        _db.Pipelines.Add(pipeline);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPipeline), new { id = pipeline.Id }, MapToResponse(pipeline));
    }

    // GET /pipelines — list all pipeline definitions
    [HttpGet]
    [ProducesResponseType(typeof(List<PipelineResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPipelines()
    {
        var pipelines = await _db.Pipelines
            .OrderBy(p => p.Name)
            .Select(p => MapToResponse(p))
            .ToListAsync();

        return Ok(pipelines);
    }

    // GET /pipelines/{id} — get a single pipeline
    [HttpGet("{id}")]
    [ProducesResponseType(typeof(PipelineResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPipeline(int id)
    {
        var pipeline = await _db.Pipelines.FindAsync(id);
        if (pipeline is null)
            return NotFound(new { error = $"Pipeline with id {id} not found." });

        return Ok(MapToResponse(pipeline));
    }

    private static PipelineResponse MapToResponse(Pipeline p) => new()
    {
        Id           = p.Id,
        Name         = p.Name,
        SourceSystem = p.SourceSystem,
        SinkSystem   = p.SinkSystem,
        Description  = p.Description,
        CreatedAt    = p.CreatedAt
    };
}
