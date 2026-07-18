// Identifies which work-tracker implementation backs the generic Work Tracking System connector (spec-020).
namespace DBAIAzure.Core.Models;

/// <summary>
/// The provider selected on the generic <see cref="ConnectorType.WorkTracker"/> connector. The name of each
/// member matches the corresponding adapter's <c>TrackerKey</c> ("AzureDevOps" / "Jira") so provider selection
/// resolves to an adapter by a direct string-key match.
/// </summary>
public enum WorkTrackerProvider
{
    /// <summary>Azure DevOps Boards — the default provider, preserved for existing deployments.</summary>
    AzureDevOps,

    /// <summary>Jira Cloud — configured entirely from the UI (spec-020).</summary>
    Jira,
}
