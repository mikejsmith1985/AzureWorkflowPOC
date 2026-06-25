// Runs a chosen saved workflow as the monitor for a running app (feature 013).
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Runs one monitoring cycle for a linked app: builds a <c>MonitoringSnapshot</c> of the app, executes
/// its linked workflow on the existing workflow-execution path, and — on a NEW detected problem —
/// creates a bounded workflow run/intake attributable to the app (close-the-loop), de-duplicated by
/// issue signature. The .NET analogue of the reference application's production-monitoring trigger.
/// </summary>
public interface IAppMonitoringService
{
    /// <summary>
    /// Runs one monitoring cycle for the app. Returns the run ids raised this cycle (possibly empty).
    /// A missing/deleted linked workflow yields an empty result without throwing (FR-017).
    /// </summary>
    Task<IReadOnlyList<string>> RunCycleAsync(MonitoredApp app, CancellationToken ct = default);
}
