using PipelineHealthMonitor.Interfaces;
using PipelineHealthMonitor.Models;

namespace PipelineHealthMonitor.Services;

public class PipelineMonitorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<PipelineMonitorWorker> _logger;

    public PipelineMonitorWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<PipelineMonitorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config       = config;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PipelineMonitorWorker started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!stoppingToken.IsCancellationRequested)
                    await DetectStuckRunsAsync(stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                    await DetectStalePipelinesAsync(stoppingToken);

                if (!stoppingToken.IsCancellationRequested)
                    await DetectVolumeAnomaliesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                // DI container disposed during shutdown -- exit cleanly
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PipelineMonitorWorker encountered an error during checks.");
            }

            try
            {
                var intervalSeconds = _config.GetValue("Monitor:IntervalSeconds", 60);
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("PipelineMonitorWorker stopped.");
    }

    private async Task DetectStuckRunsAsync(CancellationToken ct)
    {
        var timeoutMinutes = _config.GetValue("Monitor:StuckRunTimeoutMinutes", 60);
        var cutoff         = DateTime.UtcNow.AddMinutes(-timeoutMinutes);

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var stuckRuns = await uow.PipelineRuns.GetStuckRunsAsync(cutoff);
        if (!stuckRuns.Any()) return;

        _logger.LogWarning("Found {Count} stuck run(s) exceeding {Timeout}min timeout.",
            stuckRuns.Count, timeoutMinutes);

        foreach (var run in stuckRuns)
        {
            run.Status  = "FAILED";
            run.EndedAt = DateTime.UtcNow;
            run.Errors.Add(new RunError
            {
                RunId        = run.Id,
                ErrorCode    = "RUN_TIMEOUT",
                ErrorMessage = $"Run exceeded the {timeoutMinutes}-minute timeout and was automatically failed.",
                Severity     = "CRITICAL",
                OccurredAt   = DateTime.UtcNow
            });

            _logger.LogWarning("Run {RunId} for pipeline [{PipelineName}] auto-failed: stuck since {StartedAt}.",
                run.Id, run.Pipeline?.Name, run.StartedAt);
        }

        await uow.CommitAsync();
    }

    private async Task DetectStalePipelinesAsync(CancellationToken ct)
    {
        var staleHours = _config.GetValue("Monitor:StalePipelineHours", 24);
        var cutoff     = DateTime.UtcNow.AddHours(-staleHours);

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var stalePipelines = await uow.Pipelines.GetStalePipelinesAsync(cutoff);

        foreach (var pipeline in stalePipelines)
        {
            _logger.LogWarning(
                "STALE_PIPELINE: Pipeline [{PipelineId}] {PipelineName} has had no runs in the last {Hours} hours.",
                pipeline.Id, pipeline.Name, staleHours);
        }
    }

    private async Task DetectVolumeAnomaliesAsync(CancellationToken ct)
    {
        var lookback  = _config.GetValue("Monitor:VolumeAnomalyLookbackRuns", 5);
        var threshold = _config.GetValue("Monitor:VolumeAnomalyDropThreshold", 0.5);

        using var scope = _scopeFactory.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var allPipelines = await uow.Pipelines.GetAllAsync();

        foreach (var pipeline in allPipelines)
        {
            var recentRuns = await uow.PipelineRuns
                .GetRecentCompletedRunsAsync(pipeline.Id, lookback + 1);

            if (recentRuns.Count < 2) continue;

            var latestRun    = recentRuns[0];
            var baselineRuns = recentRuns.Skip(1).ToList();

            if (latestRun.RecordsRead == 0) continue;

            var latestFailureRate = (double)latestRun.RecordsFailed / latestRun.RecordsRead;
            var validBaseline     = baselineRuns.Where(r => r.RecordsRead > 0).ToList();

            if (!validBaseline.Any()) continue;

            var avgBaselineRate = validBaseline
                .Average(r => (double)r.RecordsFailed / r.RecordsRead);

            if (avgBaselineRate > 0 && latestFailureRate > avgBaselineRate * (1 + threshold))
            {
                _logger.LogWarning(
                    "VOLUME_ANOMALY: Pipeline [{PipelineId}] {PipelineName} - Run {RunId} failure rate {Current:P1} exceeds baseline {Baseline:P1}.",
                    pipeline.Id, pipeline.Name, latestRun.Id, latestFailureRate, avgBaselineRate);
            }
        }
    }
}
