// Latest monitoring-cycle health for a monitored app (feature 013).
namespace DBAIAzure.Core.Models;

/// <summary>
/// A snapshot of the most recent monitoring cycle for a <see cref="MonitoredApp"/> — surfaced as
/// per-app monitoring health (FR-013). Mirrors the reference application's trigger heartbeat.
/// </summary>
public record AppMonitoringHeartbeat(
    /// <summary>The app this heartbeat belongs to.</summary>
    string AppId,

    /// <summary>UTC instant of the most recent monitoring cycle.</summary>
    DateTimeOffset LastCycleAt,

    /// <summary>True when the most recent cycle completed without error (a no-op cycle is healthy).</summary>
    bool LastCycleOk,

    /// <summary>Diagnostic from the most recent failing cycle, or null.</summary>
    string? LastError,

    /// <summary>Total number of cycles recorded for this app.</summary>
    long CycleCount);
