using System.Text.Json.Serialization;

namespace DBAIAzure.Web.Integrations.ServiceNow;

/// <summary>
/// JSON payload sent by a ServiceNow Business Rule or Flow Designer outbound REST step.
/// Map the SNow fields you care about in the Business Rule's REST Message body to these names.
/// </summary>
public record ServiceNowWebhookPayload
{
    /// <summary>Internal sys_id — used for write-back calls after processing.</summary>
    [JsonPropertyName("sys_id")]
    public string SysId { get; init; } = string.Empty;

    /// <summary>Human-readable incident number, e.g. INC0012345.</summary>
    [JsonPropertyName("number")]
    public string Number { get; init; } = string.Empty;

    /// <summary>Short description / title of the incident.</summary>
    [JsonPropertyName("short_description")]
    public string ShortDescription { get; init; } = string.Empty;

    /// <summary>Long description / work notes body.</summary>
    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>Priority label, e.g. "1 - Critical" or "2 - High".</summary>
    [JsonPropertyName("priority")]
    public string Priority { get; init; } = string.Empty;

    /// <summary>Category field from the SNow form, e.g. "Software" or "Database".</summary>
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    /// <summary>Caller / requester display name — stored for audit trail.</summary>
    [JsonPropertyName("caller_id")]
    public string CallerId { get; init; } = string.Empty;
}
