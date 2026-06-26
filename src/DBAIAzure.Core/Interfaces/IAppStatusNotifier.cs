// In-process notification of repo-app status changes for live UI updates (feature 013).
namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Raises an event whenever a monitored app's status or results change, so the Apps UI can refresh
/// live without polling — the same in-process pattern the workflow orchestrator uses for runs.
/// </summary>
public interface IAppStatusNotifier
{
    /// <summary>Fires with the affected app id on any status/result change. Raised on a background thread.</summary>
    event Action<string>? AppStatusChanged;

    /// <summary>Signals that the given app changed; invoked by executors and the registry.</summary>
    void NotifyChanged(string appId);
}
