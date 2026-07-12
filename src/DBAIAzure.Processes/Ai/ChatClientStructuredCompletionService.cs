// Re-expresses IStructuredCompletionService atop the provider-neutral IChatClient (spec-019 T011) —
// the GA replacement for the SK-era forced-tool implementation. Lives in Processes (not Connectors)
// because it is provider-neutral (Microsoft.Extensions.AI only) and the workflow factories construct it.
using System.Text.Json;
using DBAIAzure.Core.Interfaces;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Ai;

/// <summary>
/// Produces schema-bound structured output through <see cref="IChatClient"/> by constraining the response
/// to a JSON schema (<see cref="ChatResponseFormat.ForJsonSchema(JsonElement, string, string)"/>) and
/// binding the returned JSON to the typed record. Preserves the Article VII "no free-text parsing"
/// guarantee while moving off the provider-specific SK forced-tool path — any provider whose
/// <see cref="IChatClient"/> honours a JSON-schema response format works unchanged (BYO-AI).
/// </summary>
public sealed class ChatClientStructuredCompletionService : IStructuredCompletionService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IChatClient _chatClient;

    /// <summary>Creates the structured-completion service over the active chat client.</summary>
    public ChatClientStructuredCompletionService(IChatClient chatClient) => _chatClient = chatClient;

    /// <inheritdoc />
    public async Task<T> GetStructuredAsync<T>(
        string systemPrompt,
        string userMessage,
        string toolName,
        string toolDescription,
        string inputSchemaJson,
        CancellationToken cancellationToken = default)
    {
        // The schema string arrives from the caller's contract; constrain the response to it. The tool
        // name/description carry over as the schema's name/description so the intent reaches the model.
        var schema = JsonSerializer.Deserialize<JsonElement>(inputSchemaJson);
        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, toolName, toolDescription),
        };

        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));
        }
        messages.Add(new ChatMessage(ChatRole.User, userMessage));

        var response = await _chatClient.GetResponseAsync(messages, options, cancellationToken);
        var json = (response.Text ?? string.Empty).Trim();

        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException(
                $"Structured completion for '{toolName}' returned no bindable JSON for {typeof(T).Name}.");
    }
}
