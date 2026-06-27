// The seam between the app registry and where build/run work happens (feature 013).
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Starts build/run work for an app in a throwaway, isolated container — the .NET analogue of the
/// reference application's <c>AppExecutor</c>. Two implementations exist: a simulated executor
/// (synthesizes outcomes, no container) and a real Docker executor. Both report outcomes back through
/// <see cref="IAppRegistryRepository"/> so callers never branch on which executor is active (FR-015).
/// Neither operation may leave the app stuck in Building/Running (FR-008).
/// </summary>
public interface IAppExecutor
{
    /// <summary>
    /// Builds the app's artifact for the given request. Sets status Building, then records an
    /// <see cref="AppBuildResult"/> (Ready or BuildFailed) when finished.
    /// </summary>
    Task BuildAsync(MonitoredApp app, AppExecutionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Runs the built artifact under the request's timeout. Sets status Running, then records an
    /// <see cref="AppRunResult"/> and returns the app to Ready.
    /// </summary>
    Task RunAsync(MonitoredApp app, AppExecutionRequest request, CancellationToken ct = default);

    /// <summary>
    /// A short label identifying the active executor ("Simulated" or "Docker") for the UI indicator
    /// that distinguishes demo mode from real container execution (FR-015).
    /// </summary>
    string ExecutorKind { get; }
}
