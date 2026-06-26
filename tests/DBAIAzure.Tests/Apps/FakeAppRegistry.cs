// Hand-rolled in-memory IAppRegistryRepository for fully-mocked executor/monitoring unit tests (feature 013).
using System.Collections.Concurrent;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Tests.Apps;

/// <summary>
/// A minimal in-memory <see cref="IAppRegistryRepository"/> for unit tests — no database, no I/O.
/// Records status and build/run results so executor behaviour can be asserted in isolation.
/// </summary>
internal sealed class FakeAppRegistry : IAppRegistryRepository
{
    private readonly ConcurrentDictionary<string, MonitoredApp> _apps = new();

    public IReadOnlyList<AppStatus> StatusHistory { get; } = new List<AppStatus>();

    public MonitoredApp Add(MonitoredApp app)
    {
        _apps[app.AppId] = app;
        return app;
    }

    public Task<MonitoredApp> RegisterAsync(MonitoredApp app, CancellationToken ct = default)
    {
        _apps[app.AppId] = app;
        return Task.FromResult(app);
    }

    public Task<MonitoredApp?> GetAsync(string appId, CancellationToken ct = default)
        => Task.FromResult(_apps.TryGetValue(appId, out var a) ? a : null);

    public Task<IReadOnlyList<MonitoredApp>> ListByOwnerAsync(string ownerId, CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<MonitoredApp>)_apps.Values.Where(a => a.OwnerId == ownerId).ToList());

    public Task<IReadOnlyList<MonitoredApp>> ListMonitoredAsync(CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<MonitoredApp>)_apps.Values.Where(a => a.LinkedWorkflowId != null).ToList());

    public Task SetStatusAsync(string appId, AppStatus status, CancellationToken ct = default)
    {
        ((List<AppStatus>)StatusHistory).Add(status);
        Mutate(appId, a => a with { Status = status });
        return Task.CompletedTask;
    }

    public Task SetBuildResultAsync(string appId, AppBuildResult result, CancellationToken ct = default)
    {
        var status = result.Succeeded ? AppStatus.Ready : AppStatus.BuildFailed;
        ((List<AppStatus>)StatusHistory).Add(status);
        Mutate(appId, a => a with { Status = status, LastBuildResult = result, LastBuiltAt = result.At });
        return Task.CompletedTask;
    }

    public Task SetRunResultAsync(string appId, AppRunResult result, CancellationToken ct = default)
    {
        ((List<AppStatus>)StatusHistory).Add(AppStatus.Ready);
        Mutate(appId, a => a with { Status = AppStatus.Ready, LastRunResult = result, LastRunAt = result.At });
        return Task.CompletedTask;
    }

    public Task SetLinkedWorkflowAsync(string appId, string? workflowId, CancellationToken ct = default)
    {
        Mutate(appId, a => a with { LinkedWorkflowId = workflowId });
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string appId, CancellationToken ct = default)
    {
        _apps.TryRemove(appId, out _);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsByNameAsync(string ownerId, string name, CancellationToken ct = default)
        => Task.FromResult(_apps.Values.Any(a => a.OwnerId == ownerId && a.Name == name));

    private void Mutate(string appId, Func<MonitoredApp, MonitoredApp> change)
    {
        if (_apps.TryGetValue(appId, out var existing))
            _apps[appId] = change(existing);
    }
}

/// <summary>Records app-id notifications for assertions.</summary>
internal sealed class FakeAppStatusNotifier : IAppStatusNotifier
{
    public event Action<string>? AppStatusChanged;
    public List<string> Notifications { get; } = new();
    public void NotifyChanged(string appId)
    {
        Notifications.Add(appId);
        AppStatusChanged?.Invoke(appId);
    }
}
