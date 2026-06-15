// The structured output bound from the Claude forced-tool-use call: a summary plus flagged gaps.
using System.Text.Json.Serialization;

namespace DBAIAzure.Core.Models;

/// <summary>
/// Schema-bound result of the phase validation LLM call. Bound directly from the Anthropic
/// <c>tool_use</c> block's <c>input</c>, so the property names match the tool input schema
/// (snake_case on the wire). This is what a reviewer reads before approving a work item.
/// </summary>
public record PhaseValidationResult
{
    /// <summary>One-paragraph plain-language summary of what the phase's artifacts contain.</summary>
    [JsonPropertyName("summary")]
    public required string Summary { get; init; }

    /// <summary>Flagged gaps, risks, or omissions a reviewer should see. Empty when none were found.</summary>
    [JsonPropertyName("gaps")]
    public IReadOnlyList<PhaseValidationGap> Gaps { get; init; } = [];
}

/// <summary>A single flagged gap, risk, or omission surfaced by the validation step.</summary>
public record PhaseValidationGap
{
    /// <summary>Short label naming the gap (a few words).</summary>
    [JsonPropertyName("label")]
    public required string Label { get; init; }

    /// <summary>What is missing or risky, and why it matters for the next phase.</summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }
}
