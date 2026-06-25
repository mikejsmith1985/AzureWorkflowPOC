// Close-the-loop de-duplication record for monitored-app issues (feature 013).
namespace DBAIAzure.Core.Models;

/// <summary>
/// Records that a detected problem already produced a workflow run/intake, so a recurring or ongoing
/// issue is raised once rather than every monitoring cycle (FR-012). Mirrors the reference
/// application's <c>raised_production_defects</c> registry.
/// </summary>
public record AppRaisedIssue(
    /// <summary>Stable signature hash of (app + issue type + description); the dedup key.</summary>
    string Signature,

    /// <summary>The app the issue was detected for.</summary>
    string AppId,

    /// <summary>The workflow run/intake created for this issue, or null if none was created.</summary>
    string? WorkflowRunId,

    /// <summary>UTC instant the issue was first raised.</summary>
    DateTimeOffset CreatedAt);
