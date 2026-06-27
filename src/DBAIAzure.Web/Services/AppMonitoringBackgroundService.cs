// Hosted loop that monitors linked apps and records per-app heartbeats (feature 013, US3).
using DBAIAzure.Core.Interfaces;

namespace DBAIAzure.Web.Services;

/// <summary>
/// A stock <see cref="BackgroundService"/> that, every cycle, runs the monitoring service for each
/// app with a linked workflow and records a heartbeat (last cycle time, ok/fail, error — FR-013).
/// One failing app's cycle never stops the others; a no-op cycle records a healthy heartbeat. Mirrors
/// the reference application's continuous runner + per-trigger heartbeat.
/// </summary>
public sealed class AppMonitoringBackgroundService : BackgroundService
{
    private readonly IAppRegistryRepository _registry;
    private readonly IAppMonitoringService _monitoring;
    private readonly IAppHeartbeatStore _heartbeats;
    private readonly ILogger<AppMonitoringBackgroundService> _logger;
    private readonly TimeSpan _interval;

    /// <summary>Creates the loop with a configurable cycle interval (default 60 s).</summary>
    public AppMonitoringBackgroundService(
        IAppRegistryRepository registry,
        IAppMonitoringService monitoring,
        IAppHeartbeatStore heartbeats,
        ILogger<AppMonitoringBackgroundService> logger,
        TimeSpan? interval = null)
    {
        _registry = registry;
        _monitoring = monitoring;
        _heartbeats = heartbeats;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromSeconds(60);
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOneSweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "App monitoring sweep failed this cycle — continuing.");
            }

            try { await Task.Delay(_interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task RunOneSweepAsync(CancellationToken ct)
    {
        var apps = await _registry.ListMonitoredAsync(ct);
        foreach (var app in apps)
        {
            try
            {
                await _monitoring.RunCycleAsync(app, ct);
                await _heartbeats.RecordCycleAsync(app.AppId, ok: true, error: null, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad app never stops the rest (mirrors the reference's per-trigger isolation).
                await _heartbeats.RecordCycleAsync(app.AppId, ok: false, error: ex.Message, ct);
            }
        }
    }
}
