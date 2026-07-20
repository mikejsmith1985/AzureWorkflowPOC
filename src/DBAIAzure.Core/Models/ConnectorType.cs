// Identifies which external system a connector configuration record applies to.
namespace DBAIAzure.Core.Models;

/// <summary>Discriminates the four pipeline connectors that require configuration and functional testing.</summary>
public enum ConnectorType
{
    /// <summary>ServiceNow instance — inbound ticket intake and outbound property queries.</summary>
    ServiceNow,

    /// <summary>
    /// Azure DevOps Boards — retained for backward-compatible parsing of connector rows written before the
    /// generic <see cref="WorkTracker"/> type (spec-020). No longer surfaced as its own operator-facing
    /// connector; existing rows are auto-migrated onto <see cref="WorkTracker"/> with provider = Azure DevOps.
    /// </summary>
    AzureDevOps,

    /// <summary>Language model provider (e.g., Anthropic Claude) — AI inference for phase execution.</summary>
    LLM,

    /// <summary>Messaging connector (Teams/Slack/Discord) — HITL notifications and notify-node sends.</summary>
    Messaging,

    /// <summary>
    /// Generic work-tracking system (spec-020) — a single operator-facing connector whose active provider
    /// (Azure DevOps / Jira) is chosen in the UI and stored as a discriminator in the non-secret config.
    /// Replaces the vendor-specific <see cref="AzureDevOps"/> connector identity.
    /// </summary>
    WorkTracker,

    /// <summary>
    /// DoR Validation Workflow (spec-021) — the single connector row holding all six configuration namespaces
    /// (Jira, DoR document, AI, comms, SLA, audit) plus the dry-run flag for the Definition-of-Ready workflow.
    /// Resolved per run so changes take effect without a restart.
    /// </summary>
    DorWorkflow,
}
