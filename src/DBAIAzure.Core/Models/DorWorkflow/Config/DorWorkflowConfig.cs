// The six configuration namespaces for the DoR Validation Workflow (spec-021), plus the dry-run flag. Stored as
// one non-secret JSON blob under ConnectorType.DorWorkflow; secrets are referenced by name and resolved
// separately. Resolved per run so changes take effect without a restart (FR-025).
namespace DBAIAzure.Core.Models.DorWorkflow.Config;

/// <summary>
/// The complete, runtime-resolved DoR workflow configuration. All behaviorally significant values live here so
/// they are changeable without a redeploy (FR-025). <see cref="IsConfigured"/> is false when no connector row
/// exists — the workflow then no-ops rather than failing.
/// </summary>
public sealed record DorWorkflowConfig
{
    /// <summary>Jira integration settings (project scope, fields, transition, labels).</summary>
    public DorJiraConfig Jira { get; init; } = new();

    /// <summary>Where and how the DoR document is loaded.</summary>
    public DorDocConfig Dor { get; init; } = new();

    /// <summary>AI review engine settings and prompt templates.</summary>
    public DorAiConfig Ai { get; init; } = new();

    /// <summary>Primary/escalation/success communication channels and their limits.</summary>
    public DorCommsConfig Comms { get; init; } = new();

    /// <summary>SLA durations and business-hours settings.</summary>
    public DorSlaConfig Sla { get; init; } = new();

    /// <summary>Audit/observability options.</summary>
    public DorAuditConfig Audit { get; init; } = new();

    /// <summary>Operational modes (currently the global dry-run flag — FR-032).</summary>
    public DorRunConfig Run { get; init; } = new();

    /// <summary>True when an active DoR connector row was found and parsed.</summary>
    public bool IsConfigured { get; init; }

    /// <summary>An unconfigured result — the workflow no-ops rather than failing a run.</summary>
    public static DorWorkflowConfig Unconfigured { get; } = new() { IsConfigured = false };
}

/// <summary>Jira integration namespace. Secrets (API token, webhook secret) are resolved by reference, not here.</summary>
public sealed record DorJiraConfig
{
    public string BaseUrl { get; init; } = "";
    public string AccountEmail { get; init; } = "";
    public IReadOnlyList<string> ProjectKeys { get; init; } = Array.Empty<string>();
    /// <summary>Issue types that trigger validation; empty means all types.</summary>
    public IReadOnlyList<string> IssueTypes { get; init; } = Array.Empty<string>();
    /// <summary>Fields extracted and sent to the AI for review.</summary>
    public IReadOnlyList<string> WatchFields { get; init; } = Array.Empty<string>();
    /// <summary>Human labels for field keys, used to make the review prompt readable.</summary>
    public IReadOnlyDictionary<string, string> FieldLabels { get; init; } = new Dictionary<string, string>();
    /// <summary>The strict whitelist of fields the agent may write (enforced programmatically — FR-021).</summary>
    public IReadOnlyList<string> AiEditableFields { get; init; } = Array.Empty<string>();
    public string ReadyTransitionId { get; init; } = "";
    public string ReadyStatus { get; init; } = "";
    public string ManualLabel { get; init; } = "dor-manual-required";
}

/// <summary>DoR document source namespace (source-type seam: inline / url; confluence/sharepoint deferred).</summary>
public sealed record DorDocConfig
{
    /// <summary><c>inline</c> or <c>url</c> (deferred: <c>confluence</c>, <c>sharepoint</c>).</summary>
    public string SourceType { get; init; } = "inline";
    /// <summary>URI for <c>url</c> sources.</summary>
    public string? SourceUri { get; init; }
    /// <summary>Markdown for <c>inline</c> sources.</summary>
    public string? InlineMarkdown { get; init; }
    /// <summary>Cache window before re-fetching; 0 means always fresh.</summary>
    public int CacheTtlMinutes { get; init; } = 15;
    public string Format { get; init; } = "markdown";
}

/// <summary>AI review engine namespace. The API key is resolved by reference, not stored here.</summary>
public sealed record DorAiConfig
{
    public string Provider { get; init; } = "";
    public string Model { get; init; } = "";
    public string ReviewPromptTemplate { get; init; } = "";
    public string ConversationPromptTemplate { get; init; } = "";
    public string UpdatePromptTemplate { get; init; } = "";
    public double Temperature { get; init; } = 0.1;
    public int MaxTokens { get; init; } = 2000;
}

/// <summary>Communication channels namespace.</summary>
public sealed record DorCommsConfig
{
    public DorChannelConfig Primary { get; init; } = new();
    public DorChannelConfig Escalation { get; init; } = new();
    public DorSuccessChannelConfig Success { get; init; } = new();
    /// <summary>Authors whose thread replies are ignored (e.g. other bots).</summary>
    public IReadOnlyList<string> IgnoreUserIds { get; init; } = Array.Empty<string>();
}

/// <summary>A primary or escalation channel with its reply timeout and iteration budget.</summary>
public sealed record DorChannelConfig
{
    public string Type { get; init; } = "slack";
    public string ChannelId { get; init; } = "";
    public IReadOnlyList<string> MentionUsers { get; init; } = Array.Empty<string>();
    public int ReplyTimeoutMinutes { get; init; } = 240;
    public int MaxIterations { get; init; } = 3;
}

/// <summary>Optional success-notification channel.</summary>
public sealed record DorSuccessChannelConfig
{
    public bool Enabled { get; init; } = true;
    public string? ChannelId { get; init; }
}

/// <summary>SLA configuration namespace (durations + business-hours window).</summary>
public sealed record DorSlaConfig
{
    public double PrimarySlaHours { get; init; } = 24;
    public double EscalationSlaHours { get; init; } = 8;
    /// <summary><c>business_hours</c> or <c>wall_clock</c>.</summary>
    public string ClockType { get; init; } = "business_hours";
    public DorBusinessHoursConfig BusinessHours { get; init; } = new();
}

/// <summary>Business-hours window for SLA measurement.</summary>
public sealed record DorBusinessHoursConfig
{
    public string Timezone { get; init; } = "America/Chicago";
    public string Start { get; init; } = "08:00";
    public string End { get; init; } = "17:00";
    /// <summary>Working days (Mon=1 .. Sun=7).</summary>
    public IReadOnlyList<int> WorkingDays { get; init; } = new[] { 1, 2, 3, 4, 5 };
}

/// <summary>Audit/observability namespace.</summary>
public sealed record DorAuditConfig
{
    public string StoreType { get; init; } = "jira_comment";
    public bool LogAiResponses { get; init; } = true;
    public bool JiraCommentOnPass { get; init; } = true;
    public bool JiraCommentOnFail { get; init; } = true;
    public bool JiraCommentOnEscalation { get; init; } = true;
}

/// <summary>Operational-modes namespace.</summary>
public sealed record DorRunConfig
{
    /// <summary>When true, the workflow logs intended writes/messages without performing them (FR-032).</summary>
    public bool DryRun { get; init; }
}

/// <summary>
/// The decrypted secrets for the DoR workflow, resolved server-side from the encrypted-secret store by
/// reference. Never serialized to config, logs, or the UI (Article IX / FR-026).
/// </summary>
public sealed record DorWorkflowSecrets(
    string? JiraApiToken,
    string? JiraWebhookSecret,
    string? SlackToken,
    string? AiApiKey);
