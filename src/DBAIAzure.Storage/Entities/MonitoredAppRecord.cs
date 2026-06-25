// EF Core entity: one row per registered app in the MonitoredApps SQLite table (feature 013).
namespace DBAIAzure.Storage.Entities;

/// <summary>
/// Persists one registered app. Build and run results are stored as JSON blobs (like the workflow
/// topology columns). The optional access token is never persisted here (Article IX).
/// </summary>
public sealed class MonitoredAppRecord
{
    /// <summary>Stable unique identifier (GUID string) — primary key.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>App name; unique per owner.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Owner the app belongs to.</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>Local filesystem path of the target repository.</summary>
    public string RepoLocalPath { get; set; } = string.Empty;

    /// <summary>Optional git ref to check out for the build.</summary>
    public string? Branch { get; set; }

    /// <summary>Optional build command; null triggers auto-detection.</summary>
    public string? BuildCommand { get; set; }

    /// <summary>Command that runs the built application.</summary>
    public string RunCommand { get; set; } = string.Empty;

    /// <summary>Lifecycle status stored as the <c>AppStatus</c> enum ordinal.</summary>
    public int Status { get; set; }

    /// <summary>Most recent build outcome serialized as JSON, or null.</summary>
    public string? LastBuildResultJson { get; set; }

    /// <summary>Most recent run outcome serialized as JSON, or null.</summary>
    public string? LastRunResultJson { get; set; }

    /// <summary>Identifier of the linked monitoring workflow, or null when not monitored.</summary>
    public string? LinkedWorkflowId { get; set; }

    /// <summary>UTC instant of the most recent build, or null.</summary>
    public DateTimeOffset? LastBuiltAt { get; set; }

    /// <summary>UTC instant of the most recent run, or null.</summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>UTC instant the app was registered.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC instant the row was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
