using Microsoft.AspNetCore.Mvc;
using PipelineHealthMonitor.DTOs;
using PipelineHealthMonitor.Interfaces;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Controllers;

[ApiController]
[Route("pipelines")]
public class PipelinesController : ControllerBase
{
    private readonly IUnitOfWork _uow;

    public PipelinesController(IUnitOfWork uow)
    {
        _uow = uow;
    }

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

        _uow.Pipelines.Add(pipeline);
        await _uow.CommitAsync();

        return CreatedAtAction(nameof(GetPipeline), new { id = pipeline.Id }, MapToResponse(pipeline));
    }

    [HttpGet]
    [ProducesResponseType(typeof(List<PipelineResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListPipelines()
    {
        var pipelines = await _uow.Pipelines.GetAllAsync();
        return Ok(pipelines.Select(MapToResponse).ToList());
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(PipelineResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPipeline(int id)
    {
        var pipeline = await _uow.Pipelines.FindByIdAsync(id);
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
