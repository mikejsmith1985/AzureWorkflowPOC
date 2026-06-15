using DBAIAzure.Core.Interfaces;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DBAIAzure.Connectors;

/// <summary>
/// Thin IChatCompletionService implementation backed by a direct HTTPS call to
/// the Anthropic Messages API — no SDK dependency, pure HttpClient.
///
/// Interview talking point: SK's IChatCompletionService abstraction lets you implement
/// any LLM provider in one class. Swapping to Azure OpenAI is replacing this registration
/// with AddAzureOpenAIChatCompletion() — the process steps and tracing are unchanged.
///
/// Also implements <see cref="IStructuredCompletionService"/>: a non-streaming forced tool-use
/// call that binds schema-validated JSON straight to a typed record (Article VII compliant).
/// </summary>
public sealed class AnthropicChatCompletionService : IChatCompletionService, IStructuredCompletionService, IDisposable
{
    /// <summary>Default token budget for both the free-text and structured calls.</summary>
    private const int DefaultMaxTokens = 4096;

    private readonly HttpClient _http;
    private readonly string _model;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public IReadOnlyDictionary<string, object?> Attributes { get; } =
        new Dictionary<string, object?> { { "provider", "anthropic" }, { "model_id", string.Empty } };

    public AnthropicChatCompletionService(string apiKey, string model)
    {
        _model = model;
        _http = new HttpClient { BaseAddress = new Uri("https://api.anthropic.com") };
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = chatHistory
            .Where(m => m.Role == AuthorRole.System)
            .Select(m => m.Content)
            .FirstOrDefault();

        var messages = chatHistory
            .Where(m => m.Role != AuthorRole.System)
            .Select(m => new AnthropicMessage(
                m.Role == AuthorRole.User ? "user" : "assistant",
                m.Content ?? string.Empty))
            .ToList();

        // If no user messages exist but there is content in system, treat it as user
        if (messages.Count == 0)
        {
            messages.Add(new AnthropicMessage("user", systemPrompt ?? string.Empty));
            systemPrompt = null;
        }

        var requestBody = new AnthropicRequest(_model, DefaultMaxTokens, messages, systemPrompt);
        var json = JsonSerializer.Serialize(requestBody, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("/v1/messages", content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic API error {response.StatusCode}: {responseBody}");

        var apiResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody, JsonOpts)
            ?? throw new InvalidOperationException("Empty response from Anthropic API");

        var text = apiResponse.Content?.FirstOrDefault()?.Text ?? string.Empty;
        return [new ChatMessageContent(AuthorRole.Assistant, text)];
    }

    public async IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var results = await GetChatMessageContentsAsync(chatHistory, executionSettings, kernel, cancellationToken);
        foreach (var result in results)
            yield return new StreamingChatMessageContent(result.Role, result.Content);
    }

    // ── Structured (forced tool-use) output ─────────────────────────────────────

    /// <summary>
    /// Non-streaming structured call: declares a single tool with the given input schema and forces
    /// the model to call it (tool_choice), then binds the returned tool input to <typeparamref name="T"/>.
    /// No markdown stripping and no "return ONLY JSON" prompting — the result is already structured JSON.
    /// </summary>
    public async Task<T> GetStructuredAsync<T>(
        string systemPrompt,
        string userMessage,
        string toolName,
        string toolDescription,
        string inputSchemaJson,
        CancellationToken cancellationToken = default)
    {
        // The input schema arrives as a JSON string (from the contract file); embed it as raw JSON.
        var inputSchema = JsonSerializer.Deserialize<JsonElement>(inputSchemaJson);

        var tool = new AnthropicTool(toolName, toolDescription, inputSchema);
        var toolChoice = new AnthropicToolChoice("tool", toolName);
        var messages = new List<AnthropicMessage> { new("user", userMessage) };

        var requestBody = new AnthropicStructuredRequest(
            _model, DefaultMaxTokens, messages,
            string.IsNullOrWhiteSpace(systemPrompt) ? null : systemPrompt,
            [tool], toolChoice);

        var json = JsonSerializer.Serialize(requestBody, JsonOpts);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _http.PostAsync("/v1/messages", content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Anthropic API error {response.StatusCode}: {responseBody}");

        return ParseToolUseResult<T>(responseBody, toolName);
    }

    /// <summary>
    /// Extracts the forced tool_use block matching <paramref name="toolName"/> from a raw Anthropic
    /// response body and deserialises its <c>input</c> into <typeparamref name="T"/>. Pure and
    /// HTTP-free so it can be unit-tested against a hardcoded JSON string.
    /// </summary>
    public static T ParseToolUseResult<T>(string responseBody, string toolName)
    {
        var apiResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody, JsonOpts)
            ?? throw new InvalidOperationException("Empty response from Anthropic API");

        var toolUse = apiResponse.Content?
            .FirstOrDefault(block => block.Type == "tool_use" && block.Name == toolName)
            ?? throw new InvalidOperationException(
                $"No tool_use block named '{toolName}' in the Anthropic response.");

        if (toolUse.Input is not { } input)
            throw new InvalidOperationException($"tool_use block '{toolName}' had no input payload.");

        return input.Deserialize<T>(JsonOpts)
            ?? throw new InvalidOperationException(
                $"tool_use input for '{toolName}' could not be bound to {typeof(T).Name}.");
    }

    public void Dispose() => _http.Dispose();

    // ── Wire models ───────────────────────────────────────────────────────────

    private record AnthropicRequest(
        string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        List<AnthropicMessage> Messages,
        string? System = null);

    private record AnthropicStructuredRequest(
        string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        List<AnthropicMessage> Messages,
        string? System,
        List<AnthropicTool> Tools,
        [property: JsonPropertyName("tool_choice")] AnthropicToolChoice ToolChoice);

    private record AnthropicTool(
        string Name,
        string Description,
        [property: JsonPropertyName("input_schema")] JsonElement InputSchema);

    private record AnthropicToolChoice(string Type, string Name);

    private record AnthropicMessage(string Role, string Content);

    private record AnthropicResponse(
        List<AnthropicContentBlock>? Content);

    private record AnthropicContentBlock(
        string Type,
        string? Text,
        string? Id = null,
        string? Name = null,
        JsonElement? Input = null);
}
