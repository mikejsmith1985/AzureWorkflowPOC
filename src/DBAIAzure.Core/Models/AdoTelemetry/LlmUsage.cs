// Raw per-call LLM usage reported by the connector to the usage reporter (one per Messages API call).
namespace DBAIAzure.Core.Models.AdoTelemetry;

/// <summary>
/// Usage figures for a single LLM call, as reported by the provider. Reported on both success and
/// failure (a failed call carries <see cref="IsError"/> = true and zero tokens). Captured at the
/// connector so every call path — chat completion and forced tool-use (structured) — is covered.
/// </summary>
/// <param name="ModelName">Model id from the response, or null when unavailable.</param>
/// <param name="InputTokens">Fresh (non-cached) prompt tokens.</param>
/// <param name="OutputTokens">Completion tokens.</param>
/// <param name="CacheReadTokens">Prompt tokens served from cache (the reuse signal).</param>
/// <param name="CacheCreationTokens">Prompt tokens written to cache (billed at a premium).</param>
/// <param name="IsError">True when the call failed at the provider.</param>
/// <param name="DurationMs">Wall-clock duration of the call in milliseconds.</param>
public sealed record LlmUsage(
    string? ModelName,
    int InputTokens,
    int OutputTokens,
    int CacheReadTokens,
    int CacheCreationTokens,
    bool IsError,
    long DurationMs);
