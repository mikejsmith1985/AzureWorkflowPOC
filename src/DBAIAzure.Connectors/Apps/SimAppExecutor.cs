// Simulated build/run executor — synthesizes outcomes without a real container (feature 013).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors.Apps;

/// <summary>
/// Developer/demo executor: synthesizes build and run outcomes after a short delay (no real container
/// runs). Keeps the full register → build → run → monitor flow demonstrable on a machine with no
/// container engine, mirroring the reference application's simulated executor. Never hangs.
/// Records outcomes through <see cref="IAppRegistryRepository"/> and signals the UI via
/// <see cref="IAppStatusNotifier"/> — callers never branch on which executor is active (FR-015).
/// </summary>
public sealed class SimAppExecutor : IAppExecutor
{
    private readonly IAppRegistryRepository _registry;
    private readonly IAppStatusNotifier _notifier;
    private readonly TimeSpan _delay;

    /// <summary>Creates the simulated executor. <paramref name="delay"/> defaults to a short, demo-friendly pause.</summary>
    public SimAppExecutor(IAppRegistryRepository registry, IAppStatusNotifier notifier, TimeSpan? delay = null)
    {
        _registry = registry;
        _notifier = notifier;
        _delay = delay ?? TimeSpan.FromMilliseconds(400);
    }

    /// <inheritdoc/>
    public string ExecutorKind => "Simulated";

    /// <inheritdoc/>
    public async Task BuildAsync(MonitoredApp app, AppExecutionRequest request, CancellationToken ct = default)
    {
        await _registry.SetStatusAsync(app.AppId, AppStatus.Building, ct);
        _notifier.NotifyChanged(app.AppId);

        await Task.Delay(_delay, ct);

        await _registry.SetBuildResultAsync(app.AppId, new AppBuildResult(
            Succeeded: true,
            Summary: "Build complete (simulated).",
            Logs: $"[sim] built '{app.Name}' — simulated success. Real builds run in an isolated container.\n",
            At: DateTimeOffset.UtcNow), ct);
        _notifier.NotifyChanged(app.AppId);
    }

    /// <inheritdoc/>
    public async Task RunAsync(MonitoredApp app, AppExecutionRequest request, CancellationToken ct = default)
    {
        await _registry.SetStatusAsync(app.AppId, AppStatus.Running, ct);
        _notifier.NotifyChanged(app.AppId);

        await Task.Delay(_delay, ct);

        await _registry.SetRunResultAsync(app.AppId, new AppRunResult(
            Outcome: RunOutcome.Succeeded,
            Summary: "exit 0 (simulated)",
            Logs: $"[sim] ran '{app.Name}' — simulated success. Real runs execute in an isolated container.\n",
            At: DateTimeOffset.UtcNow), ct);
        _notifier.NotifyChanged(app.AppId);
    }
}
