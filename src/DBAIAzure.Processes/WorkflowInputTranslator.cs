// Bridges the gap between plain-language test descriptions and the structured input
// that the first step of a workflow expects, using an LLM to do the translation.
#pragma warning disable SKEXP0080

using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;
using System.Text.Json;

namespace DBAIAzure.Processes;

/// <summary>
/// Translates a plain-language "I want to test with this scenario" description into two
/// artefacts that the workflow run-with-input panel needs: a structured input string
/// formatted for the first workflow step, and a one-sentence human-readable confirmation
/// so the user knows exactly what will be tested before they click Run.
/// This keeps the canvas layer free of LLM concerns — the translator owns the
/// prompt strategy and the JSON contract with the model.
/// </summary>
public sealed class WorkflowInputTranslator
{
    /// <summary>JSON property name for the model's confirmation sentence.</summary>
    private const string ConfirmationProperty = "confirmation";

    /// <summary>JSON property name for the model's structured input string.</summary>
    private const string StructuredInputProperty = "structuredInput";

    private readonly IChatCompletionService _chatService;

    /// <summary>
    /// Initialises the translator with the chat-completion backend that will do
    /// the plain-language → structured-input conversion.
    /// </summary>
    /// <param name="chatService">
    /// The Semantic Kernel <see cref="IChatCompletionService"/> implementation bound to
    /// whatever LLM connector is active for this deployment (OpenAI, Azure OpenAI, etc.).
    /// </param>
    public WorkflowInputTranslator(IChatCompletionService chatService)
    {
        _chatService = chatService;
    }

    /// <summary>
    /// Asks the LLM to interpret <paramref name="plainLanguageDescription"/> in the context
    /// of the supplied workflow nodes and return two outputs:
    /// <list type="bullet">
    ///   <item>A user-facing confirmation sentence describing exactly what will be tested.</item>
    ///   <item>A concise structured description of the data to feed into the first workflow step.</item>
    /// </list>
    /// The translation is intentionally prompt-driven so it can handle arbitrary domain
    /// language without the canvas needing to know anything about the workflow's problem domain.
    /// </summary>
    /// <param name="plainLanguageDescription">
    /// The raw test scenario the user typed, e.g. "Process an urgent ticket from a VIP customer".
    /// </param>
    /// <param name="nodes">
    /// The ordered list of nodes on the canvas. Their <see cref="WorkflowNode.Label"/> values
    /// are injected into the prompt so the model understands what pipeline it is describing input for.
    /// </param>
    /// <param name="ct">Token used to cancel the LLM call if the user navigates away.</param>
    /// <returns>
    /// A tuple of <c>(StructuredInput, Confirmation)</c> where both strings are non-null and
    /// non-empty when the LLM responds correctly.
    /// </returns>
    /// <exception cref="LlmUnavailableException">
    /// Thrown when the LLM call fails or returns a response that cannot be parsed, so callers
    /// can degrade gracefully (e.g. show an error banner) without crashing the canvas.
    /// </exception>
    public async Task<(string StructuredInput, string Confirmation)> TranslateAsync(
        string plainLanguageDescription,
        IReadOnlyList<WorkflowNode> nodes,
        CancellationToken ct = default)
    {
        var prompt = BuildPrompt(plainLanguageDescription, nodes);
        var history = new ChatHistory();
        history.AddUserMessage(prompt);

        string rawResponse;
        try
        {
            var response = await _chatService.GetChatMessageContentAsync(history, cancellationToken: ct)
                .ConfigureAwait(false);
            rawResponse = response.Content ?? string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new LlmUnavailableException(
                "The LLM could not be reached while translating the workflow input. " +
                "Check your LLM connector configuration and try again.",
                ex);
        }

        return ParseResponse(rawResponse);
    }

    // ── Prompt construction ────────────────────────────────────────────────────────

    /// <summary>
    /// Assembles the prompt that instructs the model to produce the JSON response.
    /// The node labels give the model enough context to understand what the pipeline does
    /// without exposing internal configuration details that are irrelevant to input translation.
    /// </summary>
    private static string BuildPrompt(string plainLanguageDescription, IReadOnlyList<WorkflowNode> nodes)
    {
        var stepList = nodes.Count == 0
            ? "(no steps defined)"
            : nodes.Select(node => node.Label).Aggregate((accumulated, label) => $"{accumulated}, {label}");

        var promptBuilder = new StringBuilder();
        promptBuilder.AppendLine("You are helping set up a workflow test.");
        promptBuilder.AppendLine($"The workflow has these steps: {stepList}");
        promptBuilder.AppendLine($"The user wants to test with this scenario: {plainLanguageDescription}");
        promptBuilder.AppendLine("Produce:");
        promptBuilder.AppendLine("1) A one-sentence confirmation of what will be tested (for display to user).");
        promptBuilder.AppendLine("2) A concise structured description of the input for the first workflow step.");
        promptBuilder.AppendLine("Respond as JSON with exactly these two fields:");
        promptBuilder.Append("{\"confirmation\": \"...\", \"structuredInput\": \"...\"}");

        return promptBuilder.ToString();
    }

    // ── Response parsing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the confirmation and structuredInput fields from the model's JSON response.
    /// If parsing fails for any reason the caller receives an <see cref="LlmUnavailableException"/>
    /// rather than a raw JSON exception, which keeps error handling uniform across the canvas.
    /// </summary>
    private static (string StructuredInput, string Confirmation) ParseResponse(string rawResponse)
    {
        try
        {
            // Strip markdown code fences that some models wrap around JSON responses.
            var trimmed = rawResponse.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = trimmed.IndexOf('\n');
                var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline >= 0 && lastFence > firstNewline)
                {
                    trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
                }
            }

            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;

            var confirmation = root.TryGetProperty(ConfirmationProperty, out var confirmationElement)
                ? confirmationElement.GetString() ?? string.Empty
                : string.Empty;

            var structuredInput = root.TryGetProperty(StructuredInputProperty, out var structuredInputElement)
                ? structuredInputElement.GetString() ?? string.Empty
                : string.Empty;

            if (confirmation.Length == 0 || structuredInput.Length == 0)
            {
                throw new LlmUnavailableException(
                    "The LLM returned an incomplete response — one or both required fields were empty. " +
                    "Try rephrasing your scenario description.");
            }

            return (structuredInput, confirmation);
        }
        catch (JsonException jsonException)
        {
            throw new LlmUnavailableException(
                "The LLM returned a response that could not be parsed as JSON. " +
                "Try rephrasing your scenario description.",
                jsonException);
        }
    }
}
