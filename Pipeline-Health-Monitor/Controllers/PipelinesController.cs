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

    // ASP.NET Core's DI container injects IUnitOfWork automatically.
    public PipelinesController(IUnitOfWork uow)
    {
        _uow = uow;
    }

    // POST /pipelines
    [HttpPost]
    public async Task<IActionResult> CreatePipeline([FromBody] CreatePipelineRequest request)
    {
        var pipeline = new Pipeline
        {
            Name         = request.Name,
            Description  = request.Description,
            SourceSystem = request.SourceSystem,
            SinkSystem   = request.SinkSystem,
            CreatedAt    = DateTime.UtcNow
        };

        _uow.Pipelines.Add(pipeline);
        await _uow.CommitAsync();

        var response = MapToResponse(pipeline);
        // 201 Created with a Location header pointing to GET /pipelines/{id}
        return CreatedAtAction(nameof(GetPipeline), new { id = pipeline.Id }, response);
    }

    // GET /pipelines
    [HttpGet]
    public async Task<IActionResult> GetPipelines()
    {
        var pipelines = await _uow.Pipelines.GetAllAsync();
        var responses = pipelines.Select(MapToResponse);
        return Ok(responses);
    }

    // GET /pipelines/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetPipeline(int id)
    {
        var pipeline = await _uow.Pipelines.FindByIdAsync(id);
        if (pipeline is null)
            return NotFound(new { message = $"Pipeline {id} not found." });

        return Ok(MapToResponse(pipeline));
    }

    // Maps a Pipeline entity to the outbound DTO.
    private static PipelineResponse MapToResponse(Pipeline p) => new()
    {
        Id           = p.Id,
        Name         = p.Name,
        Description  = p.Description,
        SourceSystem = p.SourceSystem,
        SinkSystem   = p.SinkSystem,
        CreatedAt    = p.CreatedAt,
        RunCount     = p.Runs.Count
    };
}
