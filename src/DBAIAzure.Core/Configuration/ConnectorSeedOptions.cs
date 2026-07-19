// Strongly-typed deploy-time seed values for the demo's back-office connectors. Bound from the
// "ConnectorSeed" configuration section (environment variables injected from the Forge Vault at
// deploy time). Deliberately has NO LLM member — the LLM key is the one credential each visitor
// supplies themselves, so it is structurally impossible to seed it (FR-004 / SC-006).
namespace DBAIAzure.Core.Configuration;

/// <summary>
/// Deploy-time configuration the <c>DemoConnectorSeeder</c> uses to pre-wire the demo's back-office
/// connectors on each cold start. Bound from the <see cref="SectionName"/> configuration section;
/// every value originates from the Forge Vault and is never committed. There is intentionally no LLM
/// entry — that key is supplied per-visitor in the UI and must never be pre-seeded.
/// </summary>
public sealed class ConnectorSeedOptions
{
    /// <summary>Configuration section these options bind from (e.g. <c>ConnectorSeed__ServiceNow__InstanceUrl</c>).</summary>
    public const string SectionName = "ConnectorSeed";

    /// <summary>ServiceNow (ticketing intake) seed values, or empty when not provided.</summary>
    public ServiceNowSeed ServiceNow { get; set; } = new();

    /// <summary>Azure DevOps (work-item tracking) seed values, or empty when not provided. Legacy: mapped
    /// onto the generic <see cref="WorkTracker"/> connector (provider = Azure DevOps) when the newer
    /// <see cref="WorkTracker"/> section is not supplied.</summary>
    public AzureDevOpsSeed AzureDevOps { get; set; } = new();

    /// <summary>Generic Work Tracking System (spec-020) seed values — provider plus that provider's fields.</summary>
    public WorkTrackerSeed WorkTracker { get; set; } = new();

    /// <summary>Messaging (Teams/Slack/Discord) seed values, or empty when not provided.</summary>
    public MessagingSeed Messaging { get; set; } = new();
}

/// <summary>
/// Seed values for the generic Work Tracking System connector (spec-020). <see cref="Provider"/> selects
/// which set of fields is used. Secrets: <see cref="PersonalAccessToken"/> (ADO) / <see cref="ApiToken"/> (Jira).
/// </summary>
public sealed class WorkTrackerSeed
{
    /// <summary>Active provider — <c>AzureDevOps</c> or <c>Jira</c> (non-secret).</summary>
    public string? Provider { get; set; }

    /// <summary>When true, overwrite an already-configured connector (used to re-point in a live test).</summary>
    public bool Overwrite { get; set; }

    // Azure DevOps fields.
    /// <summary>ADO organization URL (non-secret).</summary>
    public string? OrganizationUrl { get; set; }
    /// <summary>ADO project name (non-secret).</summary>
    public string? ProjectName { get; set; }
    /// <summary>ADO personal access token (secret).</summary>
    public string? PersonalAccessToken { get; set; }

    // Jira fields.
    /// <summary>Jira Cloud site URL, e.g. <c>https://your-org.atlassian.net</c> (non-secret).</summary>
    public string? SiteUrl { get; set; }
    /// <summary>Jira account email used with the API token (non-secret).</summary>
    public string? Email { get; set; }
    /// <summary>Jira project key, e.g. <c>PROJ</c> (non-secret).</summary>
    public string? ProjectKey { get; set; }
    /// <summary>Jira API token (secret).</summary>
    public string? ApiToken { get; set; }
}

/// <summary>Seed values for the ServiceNow connector. Secret: <see cref="Password"/>.</summary>
public sealed class ServiceNowSeed
{
    /// <summary>Instance base URL, e.g. <c>https://dev12345.service-now.com</c> (non-secret).</summary>
    public string? InstanceUrl { get; set; }

    /// <summary>Integration user name (non-secret).</summary>
    public string? Username { get; set; }

    /// <summary>Integration user password (secret — encrypted at rest by the repository).</summary>
    public string? Password { get; set; }
}

/// <summary>Seed values for the Azure DevOps connector. Secret: <see cref="PersonalAccessToken"/>.</summary>
public sealed class AzureDevOpsSeed
{
    /// <summary>Organization URL, e.g. <c>https://dev.azure.com/your-org</c> (non-secret).</summary>
    public string? OrganizationUrl { get; set; }

    /// <summary>Target project name (non-secret).</summary>
    public string? ProjectName { get; set; }

    /// <summary>Personal access token (secret — encrypted at rest by the repository).</summary>
    public string? PersonalAccessToken { get; set; }
}

/// <summary>
/// Seed values for the Messaging connector. Non-secret: platform + MCP routing fields. Secrets:
/// <see cref="WebhookUrl"/> and <see cref="McpAuthToken"/>.
/// </summary>
public sealed class MessagingSeed
{
    /// <summary>Platform name — one of <c>Teams</c>, <c>Slack</c>, or <c>Discord</c> (non-secret).</summary>
    public string? Platform { get; set; }

    /// <summary>Incoming-webhook URL for direct delivery (secret).</summary>
    public string? WebhookUrl { get; set; }

    /// <summary>MCP server endpoint for MCP-first delivery, e.g. an HTTP/SSE URL (non-secret).</summary>
    public string? McpServerUrl { get; set; }

    /// <summary>MCP tool name to invoke for a send (non-secret).</summary>
    public string? McpToolName { get; set; }

    /// <summary>MCP argument template mapping placeholders to the tool's input (non-secret).</summary>
    public string? McpArgumentTemplate { get; set; }

    /// <summary>MCP authentication token for the server (secret).</summary>
    public string? McpAuthToken { get; set; }

    /// <summary>Default target channel/recipient for a send (non-secret).</summary>
    public string? Target { get; set; }
}
