// Inbound DTO for the development-spend ingest endpoint (spec-017): a coding-agent session's usage.
using System.Text.Json.Serialization;

namespace DBAIAzure.Web.Integrations.Telemetry;

/// <summary>
/// One coding-agent session's AI usage, tagged with the ticket binding key the developer declared.
/// Token counts are re-priced via ModelPricing when <see cref="CostUsd"/> is absent.
/// </summary>
public sealed record DevUsageIngestPayload
{
    /// <summary>Canonical ticket binding key the session is bound to (required).</summary>
    [JsonPropertyName("binding_key")]
    public string? BindingKey { get; init; }

    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }

    [JsonPropertyName("cache_read_tokens")]
    public int CacheReadTokens { get; init; }

    /// <summary>Caller-supplied cost; re-priced from tokens when null.</summary>
    [JsonPropertyName("cost_usd")]
    public double? CostUsd { get; init; }

    /// <summary>The agent session id (recorded as the cost record's source).</summary>
    [JsonPropertyName("session_id")]
    public string? SessionId { get; init; }

    /// <summary>When the usage occurred; defaults to receipt time when absent.</summary>
    [JsonPropertyName("occurred_at")]
    public DateTimeOffset? OccurredAt { get; init; }
}
