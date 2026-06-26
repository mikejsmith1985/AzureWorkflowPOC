// EF Core entity: close-the-loop dedup row per raised issue signature (feature 013).
namespace DBAIAzure.Storage.Entities;

/// <summary>
/// Persists which detected issue signatures already produced a workflow run, so a recurring problem
/// is raised once rather than every monitoring cycle (FR-012).
/// </summary>
public sealed class AppRaisedIssueRecord
{
    /// <summary>Stable signature hash — primary key (the dedup key).</summary>
    public string Signature { get; set; } = string.Empty;

    /// <summary>The app the issue was detected for.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>The workflow run/intake created for this issue, or null.</summary>
    public string? WorkflowRunId { get; set; }

    /// <summary>UTC instant the issue was first raised.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
