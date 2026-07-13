// Shared LLM helpers for the MAF executors (spec-019 US1): a single-turn completion through the
// provider-neutral IChatClient and the code-fence stripping the pipeline has always applied to model JSON.
using System.Text;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// Small helpers the migrated executors share so each one issues an identical model call and parses model
/// output the same way the retired Semantic Kernel steps did (parity — FR-015). Model access is through
/// <see cref="IChatClient"/> only; no provider-specific type appears in an executor.
/// </summary>
internal static class ExecutorLlm
{
    /// <summary>
    /// Sends a single user-turn prompt and returns the trimmed assistant text — the non-streaming
    /// equivalent of the SK steps' streamed completion (token-level streaming parity is US3/T038).
    /// </summary>
    public static async Task<string> CompleteAsync(IChatClient chatClient, string prompt, CancellationToken cancellationToken)
    {
        var response = await chatClient.GetResponseAsync(
            new[] { new ChatMessage(Microsoft.Extensions.AI.ChatRole.User, prompt) }, options: null, cancellationToken);
        return (response.Text ?? string.Empty).Trim();
    }

    /// <summary>
    /// Streams a single user-turn prompt, forwarding each text chunk to <paramref name="reporter"/> as it
    /// arrives, and returns the trimmed full assistant text. This is the token-level streaming parity for the
    /// intake steps that streamed via SK's <c>GetStreamingChatMessageContentsAsync</c> (spec-019 T038): the
    /// run-bound reporter feeds <c>PipelineRun.TokenStream</c>, which the Run Detail Stream tab renders live.
    /// The final usage-only update carries no text and is skipped; cost capture still reads it in the
    /// delegating client. Falls back to no per-token reporting when <paramref name="reporter"/> is null.
    /// </summary>
    public static async Task<string> CompleteStreamingAsync(
        IChatClient chatClient,
        string prompt,
        string stepName,
        IProgressReporter? reporter,
        CancellationToken cancellationToken)
    {
        var accumulated = new StringBuilder();
        await foreach (var update in chatClient.GetStreamingResponseAsync(
            new[] { new ChatMessage(Microsoft.Extensions.AI.ChatRole.User, prompt) }, options: null, cancellationToken))
        {
            var chunk = update.Text;
            if (string.IsNullOrEmpty(chunk))
            {
                continue;
            }

            accumulated.Append(chunk);
            reporter?.ReportToken(stepName, chunk);
        }

        return accumulated.ToString().Trim();
    }

    /// <summary>
    /// Strips a leading Markdown code fence from model output, matching the exact logic the SK steps used
    /// so a fenced JSON reply parses identically. Returns the input unchanged when it is not fenced.
    /// </summary>
    public static string StripCodeFences(string text) =>
        text.StartsWith("```", StringComparison.Ordinal)
            ? string.Join('\n', text.Split('\n').Skip(1).TakeWhile(line => !line.StartsWith("```", StringComparison.Ordinal)))
            : text;
}
