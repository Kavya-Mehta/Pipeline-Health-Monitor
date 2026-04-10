using System.Diagnostics;
using PipelineHealthMonitor.Interfaces;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Services;

// BackgroundService is an abstract base class from ASP.NET Core that runs
// ExecuteAsync() in the background for the lifetime of the application.
//
// Why IServiceScopeFactory instead of injecting IUnitOfWork directly?
// BackgroundService is registered as a SINGLETON (one instance for the app's lifetime).
// IUnitOfWork is SCOPED (one instance per HTTP request, then disposed).
// You cannot inject a scoped service into a singleton — the scoped service would
// never be disposed, leaking DB connections. The factory creates a fresh scope
// each tick, resolves IUnitOfWork within it, then disposes everything cleanly.
public class PipelineMonitorWorker : BackgroundService
{
    // ActivitySource is the .NET API for creating manual spans.
    // The name must match the .AddSource("PipelineHealthMonitor.Worker") call in Program.cs —
    // that's how OpenTelemetry knows to capture spans from this source.
    private static readonly ActivitySource _activitySource =
        new("PipelineHealthMonitor.Worker");

    private readonly ILogger<PipelineMonitorWorker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;

    public PipelineMonitorWorker(
        ILogger<PipelineMonitorWorker> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PipelineMonitorWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            // Wait for the configured interval before doing work.
            try
            {
                var intervalSeconds = _configuration.GetValue<int>("Monitor:IntervalSeconds", 60);
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;  // Application is shutting down — exit cleanly.
            }

            if (stoppingToken.IsCancellationRequested) break;

            // Each tick gets a fresh scope so EF Core's DbContext is properly scoped.
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                await DetectStuckRunsAsync(uow, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                await DetectStalePipelinesAsync(uow, stoppingToken);
                if (stoppingToken.IsCancellationRequested) break;

                await DetectVolumeAnomaliesAsync(uow, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException)    { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in PipelineMonitorWorker tick");
            }
        }

        _logger.LogInformation("PipelineMonitorWorker stopped");
    }

    // Job 1: Find RUNNING runs that have been running longer than the timeout.
    // Auto-marks them FAILED and inserts a RUN_TIMEOUT error record.
    private async Task DetectStuckRunsAsync(IUnitOfWork uow, CancellationToken ct)
    {
        // StartActivity creates a span. "using" ensures it is ended (and its duration
        // recorded) when the method returns, even if an exception is thrown.
        using var activity = _activitySource.StartActivity("DetectStuckRuns");

        var timeoutMinutes = _configuration.GetValue<int>("Monitor:StuckRunTimeoutMinutes", 60);
        var cutoff = DateTime.UtcNow.AddMinutes(-timeoutMinutes);
        var stuckRuns = (await uow.PipelineRuns.GetStuckRunsAsync(cutoff)).ToList();

        // SetTag attaches key-value metadata to the span — visible in Jaeger.
        activity?.SetTag("timeout.minutes", timeoutMinutes);
        activity?.SetTag("stuck.runs.found", stuckRuns.Count);

        foreach (var run in stuckRuns)
        {
            if (ct.IsCancellationRequested) return;

            run.Status  = "FAILED";
            run.EndedAt = DateTime.UtcNow;
            uow.RunErrors.Add(new RunError
            {
                RunId      = run.Id,
                ErrorCode  = "RUN_TIMEOUT",
                Message    = $"Run exceeded {timeoutMinutes} minute timeout. Auto-marked FAILED.",
                OccurredAt = DateTime.UtcNow
            });

            _logger.LogWarning(
                "Stuck run detected and failed: RunId={RunId} PipelineId={PipelineId} StartedAt={StartedAt}",
                run.Id, run.PipelineId, run.StartedAt);
        }

        if (stuckRuns.Count > 0)
            await uow.CommitAsync();
    }

    // Job 2: Identify pipelines with no recent runs — they may have stopped working.
    private async Task DetectStalePipelinesAsync(IUnitOfWork uow, CancellationToken ct)
    {
        using var activity = _activitySource.StartActivity("DetectStalePipelines");

        var staleHours = _configuration.GetValue<int>("Monitor:StalePipelineHours", 24);
        var cutoff = DateTime.UtcNow.AddHours(-staleHours);
        activity?.SetTag("stale.threshold.hours", staleHours);
        var stalePipelines = await uow.Pipelines.GetStalePipelinesAsync(cutoff);

        foreach (var pipeline in stalePipelines)
        {
            if (ct.IsCancellationRequested) return;
            _logger.LogWarning(
                "STALE_PIPELINE: PipelineId={PipelineId} Name={Name} has not run in {Hours}h",
                pipeline.Id, pipeline.Name, staleHours);
        }
    }

    // Job 3: Check failure rate on recent runs per pipeline.
    // If failures exceed the threshold, log a VOLUME_ANOMALY warning.
    private async Task DetectVolumeAnomaliesAsync(IUnitOfWork uow, CancellationToken ct)
    {
        using var activity = _activitySource.StartActivity("DetectVolumeAnomalies");

        var threshold = _configuration.GetValue<double>("Monitor:VolumeAnomalyThreshold", 0.5);
        activity?.SetTag("anomaly.threshold", threshold);
        var pipelines = await uow.Pipelines.GetAllAsync();

        foreach (var pipeline in pipelines)
        {
            if (ct.IsCancellationRequested) return;

            var recentRuns = (await uow.PipelineRuns.GetRecentCompletedRunsAsync(pipeline.Id, 5)).ToList();
            if (recentRuns.Count < 3) continue;  // not enough data to flag

            var failureRate = (double)recentRuns.Count(r => r.Status == "FAILED") / recentRuns.Count;
            if (failureRate >= threshold)
            {
                _logger.LogWarning(
                    "VOLUME_ANOMALY: PipelineId={PipelineId} Name={Name} FailureRate={Rate:P0} in last {Count} runs",
                    pipeline.Id, pipeline.Name, failureRate, recentRuns.Count);
            }
        }
    }
}
