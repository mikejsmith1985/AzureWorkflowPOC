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
/// </summary>
public sealed class AnthropicChatCompletionService : IChatCompletionService, IDisposable
{
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

        var requestBody = new AnthropicRequest(_model, 4096, messages, systemPrompt);
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

    public void Dispose() => _http.Dispose();

    // ── Wire models ───────────────────────────────────────────────────────────

    private record AnthropicRequest(
        string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        List<AnthropicMessage> Messages,
        string? System = null);

    private record AnthropicMessage(string Role, string Content);

    private record AnthropicResponse(
        List<AnthropicContentBlock>? Content);

    private record AnthropicContentBlock(
        string Type,
        string? Text);
}
