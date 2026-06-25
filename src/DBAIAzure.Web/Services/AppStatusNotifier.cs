// In-process implementation of repo-app status change notifications (feature 013).
using DBAIAzure.Core.Interfaces;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Singleton, in-process notifier: executors and the registry call <see cref="NotifyChanged"/> after a
/// status/result change, and the Apps pages subscribe to <see cref="AppStatusChanged"/> to refresh.
/// Mirrors the workflow orchestrator's in-process <c>RunUpdated</c> event (Blazor Server is single-process).
/// </summary>
public sealed class AppStatusNotifier : IAppStatusNotifier
{
    /// <inheritdoc/>
    public event Action<string>? AppStatusChanged;

    /// <inheritdoc/>
    public void NotifyChanged(string appId) => AppStatusChanged?.Invoke(appId);
}
