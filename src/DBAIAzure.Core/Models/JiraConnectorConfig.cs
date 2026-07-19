// Non-secret configuration fields for the Jira provider of the generic Work Tracking System connector (spec-020).
namespace DBAIAzure.Core.Models;

/// <summary>
/// Non-secret fields for the Jira Cloud provider, serialized into the discriminated
/// <c>ConnectorConfig.NonSecretConfig</c> alongside the <c>provider</c> discriminator. The API token is a
/// secret and is stored in the encrypted secrets blob, never here.
/// </summary>
public record JiraConnectorConfig(
    /// <summary>Jira Cloud site base URL (e.g., <c>https://your-org.atlassian.net</c>).</summary>
    string SiteUrl,

    /// <summary>Account email used with the API token for Basic authentication.</summary>
    string Email,

    /// <summary>Project key new issues are created under (e.g., <c>PROJ</c>).</summary>
    string ProjectKey);
