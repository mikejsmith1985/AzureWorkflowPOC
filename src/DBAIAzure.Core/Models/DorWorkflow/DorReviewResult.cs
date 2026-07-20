// Structured AI outputs for the DoR workflow (spec-021): the review verdict, a human-reply evaluation, and a
// field-update payload. Property names match the forced-tool JSON schemas in the AI-prompt contract.
using System.Text.Json.Serialization;

namespace DBAIAzure.Core.Models.DorWorkflow;

/// <summary>The AI's verdict for a ticket against the DoR: overall result, per-criterion detail, gaps, and
/// suggested field values (suggested values are advisory — the whitelist is enforced in code, not here).</summary>
public sealed record DorReviewResult(
    [property: JsonPropertyName("overall")] string Overall,
    [property: JsonPropertyName("criteria")] IReadOnlyList<CriterionResult> Criteria,
    [property: JsonPropertyName("missing_fields")] IReadOnlyList<string> MissingFields,
    [property: JsonPropertyName("ai_suggested_updates")] IReadOnlyDictionary<string, string> SuggestedUpdates)
{
    /// <summary>True when the overall verdict is a pass (case-insensitive).</summary>
    [JsonIgnore]
    public bool IsPass => string.Equals(Overall, "PASS", StringComparison.OrdinalIgnoreCase);
}

/// <summary>One DoR criterion's outcome.</summary>
public sealed record CriterionResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reason")] string Reason);

/// <summary>The AI's interpretation of a human reply against the outstanding gaps (spec-021 conversation loop).</summary>
public sealed record ReplyEvaluation(
    [property: JsonPropertyName("resolved")] bool Resolved,
    [property: JsonPropertyName("remaining_gaps")] IReadOnlyList<string> RemainingGaps,
    [property: JsonPropertyName("field_updates")] IReadOnlyDictionary<string, string> FieldUpdates,
    [property: JsonPropertyName("reply_message")] string ReplyMessage);

/// <summary>A Jira field-update body built by the AI from a resolution (filtered to the whitelist before use).</summary>
public sealed record FieldUpdatePayload(
    [property: JsonPropertyName("fields")] IReadOnlyDictionary<string, string> Fields);
