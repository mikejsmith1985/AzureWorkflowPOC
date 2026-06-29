// Inbound DTOs for the Spec Kit webhook endpoints (phase-complete signal + approval callback).
using System.Text.Json.Serialization;

namespace DBAIAzure.Web.Integrations.SpecKit;

/// <summary>
/// The phase-complete signal body (POST /api/webhook/speckit-phase). Identifies the feature and the
/// completed phase; the optional directory is derived from the key under the specs root if omitted.
/// </summary>
public sealed record PhaseSignalPayload
{
    /// <summary>Stable feature slug; idempotency key part 1 (required).</summary>
    [JsonPropertyName("feature_key")]
    public string? FeatureKey { get; init; }

    /// <summary>Repo-relative feature directory; derived from the key if omitted.</summary>
    [JsonPropertyName("feature_directory")]
    public string? FeatureDirectory { get; init; }

    /// <summary>Completed phase (specify | plan | implement; others recorded Unsupported).</summary>
    [JsonPropertyName("phase")]
    public string? Phase { get; init; }

    /// <summary>
    /// Identity that triggered this phase signal (e.g. the developer or automation that advanced the
    /// spec). Optional and self-asserted by the (secret-gated) caller — used to attribute AI usage on
    /// the resulting work item. Falls back to the approver when omitted.
    /// </summary>
    [JsonPropertyName("triggered_by")]
    public string? TriggeredBy { get; init; }
}

/// <summary>
/// The approval-decision body (POST /api/webhook/speckit-approval), delivered by the decision card.
/// </summary>
public sealed record ApprovalDecisionPayload
{
    /// <summary>The run id returned by the phase-complete endpoint (required).</summary>
    [JsonPropertyName("run_id")]
    public string? RunId { get; init; }

    /// <summary>True = create/upsert the work item; false = reject (required).</summary>
    [JsonPropertyName("approved")]
    public bool? Approved { get; init; }

    /// <summary>Reviewer identity, recorded for audit.</summary>
    [JsonPropertyName("decided_by")]
    public string? DecidedBy { get; init; }

    /// <summary>Optional note appended to the work item discussion comment.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; init; }
}
