// Non-secret configuration fields for the Jira provider of the generic Work Tracking System connector (spec-020),
// extended with the MCP-first transport and trigger settings that let Jira be reached the same way Slack is.
namespace DBAIAzure.Core.Models;

/// <summary>
/// Non-secret fields for the Jira Cloud provider, serialized into the discriminated
/// <c>ConnectorConfig.NonSecretConfig</c> alongside the <c>provider</c> discriminator. The API token and the
/// inbound webhook signing secret are secrets and live in the encrypted secrets blob, never here.
///
/// Access is MCP-first, matching the Messaging connector: when <see cref="McpServerUrl"/> is set, reads,
/// status transitions, and the ticket-created trigger go through the configured MCP tools; the direct Jira REST
/// API is the fallback for those operations and remains the only path for issue creation and comments.
/// </summary>
public record JiraConnectorConfig(
    /// <summary>Jira Cloud site base URL (e.g., <c>https://your-org.atlassian.net</c>).</summary>
    string SiteUrl,

    /// <summary>Account email used with the API token for Basic authentication.</summary>
    string Email,

    /// <summary>Project key new issues are created under (e.g., <c>PROJ</c>).</summary>
    string ProjectKey,

    /// <summary>Remote MCP server endpoint (HTTP/SSE). When non-empty, the MCP path is preferred over REST.</summary>
    string? McpServerUrl = null,

    /// <summary>Name of the MCP tool that reads one issue (e.g. <c>getJiraIssue</c>). Blank disables MCP reads.</summary>
    string? McpReadToolName = null,

    /// <summary>
    /// JSON arguments for the read tool, using <c>{{issueKey}}</c> and <c>{{fields}}</c> placeholders.
    /// Blank uses a generic default; set it explicitly for servers that need extra arguments such as a cloud id.
    /// </summary>
    string? McpReadArgumentTemplate = null,

    /// <summary>Name of the MCP tool that transitions an issue (e.g. <c>transitionJiraIssue</c>). Blank disables MCP transitions.</summary>
    string? McpTransitionToolName = null,

    /// <summary>JSON arguments for the transition tool, using <c>{{issueKey}}</c> and <c>{{transitionId}}</c> placeholders.</summary>
    string? McpTransitionArgumentTemplate = null,

    /// <summary>Name of the MCP tool that runs a JQL search (e.g. <c>searchJiraIssuesUsingJql</c>). Blank disables the MCP trigger poll.</summary>
    string? McpSearchToolName = null,

    /// <summary>JSON arguments for the search tool, using <c>{{jql}}</c> and <c>{{maxResults}}</c> placeholders.</summary>
    string? McpSearchArgumentTemplate = null,

    /// <summary>
    /// How often the MCP trigger poll asks for newly-created tickets. Zero or negative turns the poll off, leaving
    /// the inbound webhook as the only trigger. Ignored when no search tool is configured.
    /// </summary>
    int TriggerPollSeconds = 0);
