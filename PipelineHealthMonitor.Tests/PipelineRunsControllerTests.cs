using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using PipelineHealthMonitor.Controllers;
using PipelineHealthMonitor.DTOs;
using PipelineHealthMonitor.Interfaces;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Tests;

public class PipelineRunsControllerTests
{
    // Creates a controller with a minimal ControllerContext so ASP.NET helpers
    // (used internally by CreatedAtAction) do not throw NullReferenceException.
    private static PipelineRunsController BuildController(IUnitOfWork uow)
    {
        return new PipelineRunsController(uow)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            },
            Url = Mock.Of<IUrlHelper>()
        };
    }

    // Creates all four mocks and wires the repository mocks onto IUnitOfWork.
    private static (
        Mock<IUnitOfWork> uow,
        Mock<IPipelineRepository> pipelines,
        Mock<IPipelineRunRepository> runs,
        Mock<IRunErrorRepository> errors
    ) BuildMocks()
    {
        var mockPipelines = new Mock<IPipelineRepository>();
        var mockRuns      = new Mock<IPipelineRunRepository>();
        var mockErrors    = new Mock<IRunErrorRepository>();
        var mockUow       = new Mock<IUnitOfWork>();

        mockUow.Setup(u => u.Pipelines).Returns(mockPipelines.Object);
        mockUow.Setup(u => u.PipelineRuns).Returns(mockRuns.Object);
        mockUow.Setup(u => u.RunErrors).Returns(mockErrors.Object);
        mockUow.Setup(u => u.CommitAsync()).ReturnsAsync(1);

        return (mockUow, mockPipelines, mockRuns, mockErrors);
    }

    // [Fact] = a single test case. xUnit discovers any method with this attribute.
    // Arrange/Act/Assert is the universal pattern: set up, call, verify.

    [Fact]
    public async Task CreateRun_WhenPipelineNotFound_ReturnsNotFound()
    {
        // Arrange
        var (mockUow, mockPipelines, _, _) = BuildMocks();
        mockPipelines
            .Setup(r => r.FindByIdAsync(It.IsAny<int>()))
            .ReturnsAsync((Pipeline?)null);   // simulate: pipeline does not exist

        var controller = BuildController(mockUow.Object);
        var request    = new StartRunRequest { PipelineId = 99, TriggeredBy = "test" };

        // Act
        var result = await controller.StartRun(request);

        // Assert -- is<T> pattern: asserts type AND gets typed reference
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task CreateRun_WhenValid_Returns201Created()
    {
        // Arrange
        var (mockUow, mockPipelines, mockRuns, _) = BuildMocks();

        var existingPipeline = new Pipeline
        {
            Id = 1, Name = "orders-to-warehouse",
            SourceSystem = "orders-db", SinkSystem = "warehouse"
        };

        mockPipelines.Setup(r => r.FindByIdAsync(1)).ReturnsAsync(existingPipeline);
        mockRuns.Setup(r => r.Add(It.IsAny<PipelineRun>())).Verifiable();

        var controller = BuildController(mockUow.Object);
        var request    = new StartRunRequest { PipelineId = 1, TriggeredBy = "scheduler" };

        // Act
        var result = await controller.StartRun(request);

        // Assert
        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        // Verify the run was added and the transaction was committed
        mockRuns.Verify(r => r.Add(It.IsAny<PipelineRun>()), Times.Once);
        mockUow.Verify(u => u.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task GetRunById_WhenNotFound_ReturnsNotFound()
    {
        var (mockUow, _, mockRuns, _) = BuildMocks();
        mockRuns
            .Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<int>()))
            .ReturnsAsync((PipelineRun?)null);

        var result = await BuildController(mockUow.Object).GetRun(999);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetRunById_WhenFound_ReturnsOkWithRun()
    {
        var (mockUow, _, mockRuns, _) = BuildMocks();

        var run = new PipelineRun
        {
            Id = 42, PipelineId = 1, Status = "SUCCESS",
            StartedAt = DateTime.UtcNow.AddMinutes(-5), EndedAt = DateTime.UtcNow,
            Pipeline = new Pipeline { Id = 1, Name = "orders", SourceSystem = "src", SinkSystem = "sink" },
            Errors   = new List<RunError>()
        };

        mockRuns.Setup(r => r.GetByIdWithDetailsAsync(42)).ReturnsAsync(run);

        var result = await BuildController(mockUow.Object).GetRun(42);

        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PipelineRunResponse>(ok.Value);
        Assert.Equal(42, response.Id);
        Assert.Equal("SUCCESS", response.Status);
    }

    [Fact]
    public async Task GetHealth_WhenNoRuns_ReturnsHealthyStatus()
    {
        var (mockUow, mockPipelines, mockRuns, mockErrors) = BuildMocks();

        mockPipelines.Setup(r => r.CountAsync()).ReturnsAsync(0);
        mockRuns.Setup(r => r.CountAsync()).ReturnsAsync(0);
        // It.IsAny<string>() matches any string -- one setup covers all four status calls
        mockRuns.Setup(r => r.CountByStatusAsync(It.IsAny<string>())).ReturnsAsync(0);
        mockRuns.Setup(r => r.CountFailedSinceAsync(It.IsAny<DateTime>())).ReturnsAsync(0);
        mockRuns.Setup(r => r.SumRecordsReadForCompletedAsync()).ReturnsAsync(0L);
        mockErrors.Setup(r => r.CountCriticalSinceAsync(It.IsAny<DateTime>())).ReturnsAsync(0);

        var result = await BuildController(mockUow.Object).GetHealth();

        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<HealthResponse>(ok.Value);
        // No FAILED runs in last 24h + no PARTIAL runs = HEALTHY
        Assert.Equal("HEALTHY", response.OverallHealth);
        Assert.Equal(0, response.TotalRuns);
    }

    [Fact]
    public async Task GetHealth_WhenRecentFailures_ReturnsCriticalStatus()
    {
        var (mockUow, mockPipelines, mockRuns, mockErrors) = BuildMocks();

        mockPipelines.Setup(r => r.CountAsync()).ReturnsAsync(1);
        mockRuns.Setup(r => r.CountAsync()).ReturnsAsync(10);
        mockRuns.Setup(r => r.CountByStatusAsync(It.IsAny<string>())).ReturnsAsync(0);
        // 3 failures in the last 24h -> should produce CRITICAL
        mockRuns.Setup(r => r.CountFailedSinceAsync(It.IsAny<DateTime>())).ReturnsAsync(3);
        mockRuns.Setup(r => r.SumRecordsReadForCompletedAsync()).ReturnsAsync(5000L);
        mockErrors.Setup(r => r.CountCriticalSinceAsync(It.IsAny<DateTime>())).ReturnsAsync(1);

        var result = await BuildController(mockUow.Object).GetHealth();

        var ok       = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<HealthResponse>(ok.Value);
        Assert.Equal("CRITICAL", response.OverallHealth);
        Assert.Equal(3, response.FailuresLast24Hours);
    }

    [Fact]
    public async Task CompleteRun_WhenRunNotFound_ReturnsNotFound()
    {
        var (mockUow, _, mockRuns, _) = BuildMocks();
        mockRuns
            .Setup(r => r.GetByIdWithDetailsAsync(It.IsAny<int>()))
            .ReturnsAsync((PipelineRun?)null);

        var result = await BuildController(mockUow.Object)
            .CompleteRun(999, new CompleteRunRequest { Status = "SUCCESS" });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task CompleteRun_WhenRunAlreadyCompleted_ReturnsBadRequest()
    {
        var (mockUow, _, mockRuns, _) = BuildMocks();

        // Run is already SUCCESS -- completing it again must return 400
        var completedRun = new PipelineRun
        {
            Id = 5, Status = "SUCCESS",
            Pipeline = new Pipeline { Id = 1, Name = "p", SourceSystem = "s", SinkSystem = "t" },
            Errors   = new List<RunError>()
        };

        mockRuns.Setup(r => r.GetByIdWithDetailsAsync(5)).ReturnsAsync(completedRun);

        var result = await BuildController(mockUow.Object)
            .CompleteRun(5, new CompleteRunRequest { Status = "FAILED" });

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
