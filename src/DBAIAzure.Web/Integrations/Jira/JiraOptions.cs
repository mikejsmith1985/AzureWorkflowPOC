// Connection settings for the Jira work-tracker adapter (spec-018). Secrets resolved via config/vault.
namespace DBAIAzure.Web.Integrations.Jira;

/// <summary>
/// Jira Cloud connection settings, bound from the <c>WorkTracker:Jira</c> configuration section. The API
/// token is a secret (env/vault); it is used only to build the Basic auth header at HttpClient setup.
/// </summary>
public sealed class JiraOptions
{
    /// <summary>Jira Cloud site base URL, e.g. <c>https://your-org.atlassian.net</c>.</summary>
    public string SiteUrl { get; set; } = string.Empty;

    /// <summary>Account email used with the API token for Basic auth.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>API token (secret).</summary>
    public string ApiToken { get; set; } = string.Empty;

    /// <summary>Project key new issues are created under, e.g. <c>PROJ</c>.</summary>
    public string ProjectKey { get; set; } = string.Empty;
}
