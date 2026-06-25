// EF Core entity: one heartbeat row per monitored app (feature 013).
namespace DBAIAzure.Storage.Entities;

/// <summary>Persists the latest monitoring-cycle health for an app (one row per app).</summary>
public sealed class AppMonitoringHeartbeatRecord
{
    /// <summary>The app this heartbeat belongs to — primary key.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>UTC instant of the most recent monitoring cycle.</summary>
    public DateTimeOffset LastCycleAt { get; set; }

    /// <summary>True when the most recent cycle completed without error.</summary>
    public bool LastCycleOk { get; set; }

    /// <summary>Diagnostic from the most recent failing cycle, or null.</summary>
    public string? LastError { get; set; }

    /// <summary>Total number of cycles recorded for this app.</summary>
    public long CycleCount { get; set; }
}
