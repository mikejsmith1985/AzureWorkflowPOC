// Captures each model call's token usage at the IChatClient seam and reports it to the existing usage
// reporter — the MAF/M.E.AI replacement for the two Semantic Kernel cost filters (spec-019 T010, D8/D9).
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Web.Services.Ai;

/// <summary>
/// A <see cref="DelegatingChatClient"/> that meters every model call and re-homes cost/telemetry capture
/// from the retired Semantic Kernel filters (<c>IFunctionInvocationFilter</c> + <c>IPromptRenderFilter</c>)
/// onto the model call itself — the correct seam under MAF/M.E.AI, where token usage lives on the
/// response rather than on a function hook (spec-019 D8/D9).
///
/// It reads <see cref="ChatResponse.Usage"/> (for streaming, the <see cref="UsageContent"/> on the final
/// update), maps it to the existing <see cref="LlmUsage"/>, and reports it through the existing
/// <see cref="ILlmUsageReporter"/> — so the cost ledger, binding key, and ingest downstream are fed
/// exactly as before (SC-004 parity). Capture is best-effort: a metering failure never disrupts the call.
/// </summary>
public sealed class CostCapturingChatClient : DelegatingChatClient
{
    // Anthropic reports cache-write tokens under this extra usage count; M.E.AI surfaces it in
    // UsageDetails.AdditionalCounts (CachedInputTokenCount already carries the cache-read figure).
    private const string CacheCreationTokensKey = "cache_creation_input_tokens";

    // Used only when neither the response nor the request options name a model.
    private const string UnknownModelName = "unknown";

    private readonly ILlmUsageReporter _usageReporter;
    private readonly ILogger<CostCapturingChatClient> _logger;

    /// <summary>Wraps an inner chat client so its usage is metered and reported without altering results.</summary>
    public CostCapturingChatClient(
        IChatClient innerClient,
        ILlmUsageReporter usageReporter,
        ILogger<CostCapturingChatClient> logger)
        : base(innerClient)
    {
        _usageReporter = usageReporter;
        _logger = logger;
    }

    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        LogPromptHash(messages);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await base.GetResponseAsync(messages, options, cancellationToken);
            stopwatch.Stop();

            var modelName = response.ModelId ?? options?.ModelId ?? UnknownModelName;
            _usageReporter.Report(MapUsage(response.Usage, modelName, stopwatch.ElapsedMilliseconds));
            return response;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            ReportError(options?.ModelId, stopwatch.ElapsedMilliseconds, exception);
            throw;
        }
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        LogPromptHash(messages);
        var stopwatch = Stopwatch.StartNew();
        UsageDetails? capturedUsage = null;
        var modelName = options?.ModelId;

        // Enumerate manually so a mid-stream failure is still metered (an iterator's yield cannot sit
        // inside a try/catch), while the final usage update is captured for the success report.
        var updates = base.GetStreamingResponseAsync(messages, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                ChatResponseUpdate update;
                try
                {
                    if (!await updates.MoveNextAsync())
                    {
                        break;
                    }

                    update = updates.Current;
                }
                catch (Exception exception)
                {
                    stopwatch.Stop();
                    ReportError(modelName, stopwatch.ElapsedMilliseconds, exception);
                    throw;
                }

                if (update.ModelId is { Length: > 0 } id)
                {
                    modelName = id;
                }

                var usage = update.Contents.OfType<UsageContent>().FirstOrDefault()?.Details;
                if (usage is not null)
                {
                    capturedUsage = usage;
                }

                yield return update;
            }
        }
        finally
        {
            await updates.DisposeAsync();
        }

        stopwatch.Stop();
        _usageReporter.Report(MapUsage(capturedUsage, modelName ?? UnknownModelName, stopwatch.ElapsedMilliseconds));
    }

    /// <summary>Maps M.E.AI <see cref="UsageDetails"/> onto the existing <see cref="LlmUsage"/> record.</summary>
    private static LlmUsage MapUsage(UsageDetails? usage, string modelName, long durationMs)
    {
        if (usage is null)
        {
            return new LlmUsage(modelName, 0, 0, 0, 0, IsError: false, DurationMs: durationMs);
        }

        var cacheCreationTokens = 0;
        if (usage.AdditionalCounts is { } extraCounts &&
            extraCounts.TryGetValue(CacheCreationTokensKey, out var creationCount))
        {
            cacheCreationTokens = (int)creationCount;
        }

        return new LlmUsage(
            ModelName:           modelName,
            InputTokens:         (int)(usage.InputTokenCount ?? 0),
            OutputTokens:        (int)(usage.OutputTokenCount ?? 0),
            CacheReadTokens:     (int)(usage.CachedInputTokenCount ?? 0),
            CacheCreationTokens: cacheCreationTokens,
            IsError:             false,
            DurationMs:          durationMs);
    }

    /// <summary>Reports a failed call: no token data, model if known, so the failure is still counted.</summary>
    private void ReportError(string? modelName, long durationMs, Exception exception)
    {
        _logger.LogWarning(exception, "Model call failed after {DurationMs}ms; recording an error usage event.", durationMs);
        _usageReporter.Report(new LlmUsage(
            modelName ?? UnknownModelName, 0, 0, 0, 0, IsError: true, DurationMs: durationMs));
    }

    /// <summary>
    /// Logs only the SHA-256 hash of the rendered prompt (never the text) so a call can be correlated
    /// without storing sensitive content — the replacement for the SK <c>WorkflowPromptRenderFilter</c>
    /// (Article IX — secrets/content never enter a log).
    /// </summary>
    private void LogPromptHash(IEnumerable<ChatMessage> messages)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        var rendered = string.Join("\n", messages.Select(message => message.Text));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rendered));
        _logger.LogDebug("ModelCall prompt sha256={Hash}", Convert.ToHexString(hash)[..12]);
    }
}
