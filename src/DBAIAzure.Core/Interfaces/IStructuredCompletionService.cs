// Seam for schema-bound LLM output: forces a tool call and binds the result to a typed record.
namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Produces structured, schema-bound output from an LLM and binds it directly to a typed record.
/// Closes the Article VII "no free-text parsing" gap: the implementation forces a single tool
/// call (Anthropic forced tool use) and deserialises the tool input into <typeparamref name="T"/>.
/// Registered alongside <c>IChatCompletionService</c> in the kernel so steps resolve it the same way;
/// unit tests supply a fake so no network call is made.
/// </summary>
public interface IStructuredCompletionService
{
    /// <summary>
    /// Sends the system prompt + user message with a single forced tool whose input schema is
    /// <paramref name="inputSchemaJson"/>, then binds the returned tool input to
    /// <typeparamref name="T"/>. Non-streaming — a forced single tool block has nothing to stream.
    /// </summary>
    /// <typeparam name="T">The record the tool input is deserialised into.</typeparam>
    Task<T> GetStructuredAsync<T>(
        string systemPrompt,
        string userMessage,
        string toolName,
        string toolDescription,
        string inputSchemaJson,
        CancellationToken cancellationToken = default);
}
