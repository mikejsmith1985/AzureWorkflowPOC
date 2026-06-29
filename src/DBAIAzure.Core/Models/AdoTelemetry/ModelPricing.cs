// Estimates the USD cost of a run's token usage for the AIEstimatedCostUSD telemetry field.
namespace DBAIAzure.Core.Models.AdoTelemetry;

/// <summary>
/// Estimates the cost of LLM usage from token counts. The rates are APPROXIMATE Anthropic list prices
/// in USD per 1,000,000 tokens and exist only to populate the "AI Estimated Cost USD" field — they are
/// not billing-accurate and must be kept in sync with the published pricing at anthropic.com/pricing.
/// An unknown model returns null (no estimate) rather than a misleading zero.
/// </summary>
public static class ModelPricing
{
    /// <summary>Per-model-tier price in USD per one million input and output tokens.</summary>
    private sealed record TokenRate(decimal InputPerMillionUsd, decimal OutputPerMillionUsd);

    private const decimal TokensPerMillion = 1_000_000m;
    private const int CostDecimalPlaces = 4;

    // Matched by case-insensitive substring of the model name (e.g. "claude-opus-4-8" → opus tier).
    private static readonly IReadOnlyList<(string ModelNameContains, TokenRate Rate)> Tiers = new[]
    {
        ("opus",   new TokenRate(15m, 75m)),
        ("sonnet", new TokenRate(3m, 15m)),
        ("haiku",  new TokenRate(0.80m, 4m)),
        ("fable",  new TokenRate(3m, 15m)),
    };

    /// <summary>
    /// Returns the estimated cost in USD for the given token usage, or null when the model name is
    /// blank or does not match a known pricing tier (so callers can omit the field rather than write 0).
    /// </summary>
    public static double? EstimateCostUsd(string? modelName, int inputTokens, int outputTokens)
    {
        if (string.IsNullOrWhiteSpace(modelName))
            return null;

        var normalized = modelName.ToLowerInvariant();
        foreach (var (modelNameContains, rate) in Tiers)
        {
            if (!normalized.Contains(modelNameContains))
                continue;

            var estimated = (inputTokens / TokensPerMillion) * rate.InputPerMillionUsd
                          + (outputTokens / TokensPerMillion) * rate.OutputPerMillionUsd;
            return (double)Math.Round(estimated, CostDecimalPlaces);
        }

        return null;
    }
}
