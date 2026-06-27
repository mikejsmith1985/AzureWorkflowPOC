// Bound configuration for the Azure DevOps Boards destination (org/project/area/iteration + PAT).
namespace DBAIAzure.Web.Integrations.AzureDevOps;

/// <summary>
/// Strongly-typed Azure DevOps configuration bound from the <c>AzureDevOps</c> section. The PAT is a
/// secret resolved from configuration only — never hard-coded or logged (Article IX / FR-016).
/// </summary>
public sealed class AzureDevOpsOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "AzureDevOps";

    /// <summary>Organisation collection URL, e.g. <c>https://dev.azure.com/your-org</c>.</summary>
    public string OrganizationUrl { get; set; } = string.Empty;

    /// <summary>Target project name.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>Optional area path for created work items.</summary>
    public string AreaPath { get; set; } = string.Empty;

    /// <summary>Optional iteration path for created work items.</summary>
    public string IterationPath { get; set; } = string.Empty;

    /// <summary>Personal access token with Work Items (Read &amp; Write) scope (secret).</summary>
    public string Pat { get; set; } = string.Empty;
}
