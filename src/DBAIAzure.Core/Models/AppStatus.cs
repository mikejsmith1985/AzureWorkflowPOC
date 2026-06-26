// Lifecycle state of a registered repo-app (feature 013). Mirrors the reference app's status machine.
namespace DBAIAzure.Core.Models;

/// <summary>
/// The lifecycle state of a <see cref="MonitoredApp"/>. Transitions mirror the reference application's
/// app status machine: Registered → Building → (Ready | BuildFailed); Ready → Running → Ready.
/// An app is never left stuck in Building/Running — a timeout or start failure always resolves to a
/// recorded terminal-for-the-operation status.
/// </summary>
public enum AppStatus
{
    /// <summary>Row exists, not yet built.</summary>
    Registered = 0,

    /// <summary>A build is in progress in a throwaway container.</summary>
    Building = 1,

    /// <summary>Built artifact present; the app is runnable on demand.</summary>
    Ready = 2,

    /// <summary>The last build attempt failed; a summary and logs were captured.</summary>
    BuildFailed = 3,

    /// <summary>A run is in progress (transient — returns to <see cref="Ready"/> on completion).</summary>
    Running = 4
}
