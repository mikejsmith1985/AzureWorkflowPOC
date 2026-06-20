// Identifies which external system a connector configuration record applies to.
namespace DBAIAzure.Core.Models;

/// <summary>Discriminates the four pipeline connectors that require configuration and functional testing.</summary>
public enum ConnectorType
{
    /// <summary>ServiceNow instance — inbound ticket intake and outbound property queries.</summary>
    ServiceNow,

    /// <summary>Azure DevOps Boards — work item creation and project management.</summary>
    AzureDevOps,

    /// <summary>Language model provider (e.g., Anthropic Claude) — AI inference for phase execution.</summary>
    LLM,

    /// <summary>Microsoft Teams channel — HITL notifications and approval routing.</summary>
    Teams,
}
