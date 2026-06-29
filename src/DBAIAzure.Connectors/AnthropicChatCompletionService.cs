using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
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

    // Optional usage sink — when set, every Messages API call (chat or structured) is reported here so
    // it can be recorded as run-correlated telemetry. Null keeps the connector standalone/testable.
    private readonly ILlmUsageReporter? _usageReporter;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public IReadOnlyDictionary<string, object?> Attributes { get; } =
        new Dictionary<string, object?> { { "provider", "anthropic" }, { "model_id", string.Empty } };

    public AnthropicChatCompletionService(string apiKey, string model, ILlmUsageReporter? usageReporter = null)
    {
        _model = model;
        _usageReporter = usageReporter;
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

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await _http.PostAsync("/v1/messages", content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            ReportUsage(responseBody: null, stopwatch.ElapsedMilliseconds, isError: true);
            throw new HttpRequestException($"Anthropic API error {response.StatusCode}: {responseBody}");
        }

        var apiResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody, JsonOpts)
            ?? throw new InvalidOperationException("Empty response from Anthropic API");

        ReportUsage(responseBody, stopwatch.ElapsedMilliseconds, isError: false);

        var text = apiResponse.Content?.FirstOrDefault()?.Text ?? string.Empty;
        return [new ChatMessageContent(AuthorRole.Assistant, text)];
    }

    /// <summary>
    /// Builds an <see cref="LlmUsage"/> from a raw Anthropic Messages response body (the success case),
    /// pulling token counts incl. both cache figures and the model. Public static so usage parsing is
    /// unit-testable without an HTTP round-trip (mirrors <see cref="ParseToolUseResult{T}"/>).
    /// </summary>
    public static LlmUsage BuildUsage(string responseBody, string fallbackModel, long durationMs)
    {
        var apiResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody, JsonOpts);
        var usage = apiResponse?.Usage;
        return new LlmUsage(
            ModelName:           apiResponse?.Model ?? fallbackModel,
            InputTokens:         usage?.InputTokens ?? 0,
            OutputTokens:        usage?.OutputTokens ?? 0,
            CacheReadTokens:     usage?.CacheReadTokens ?? 0,
            CacheCreationTokens: usage?.CacheCreationTokens ?? 0,
            IsError:             false,
            DurationMs:          durationMs);
    }

    /// <summary>Reports an LLM call's usage (success → parsed body; failure → zeroed, IsError).</summary>
    private void ReportUsage(string? responseBody, long durationMs, bool isError)
    {
        if (_usageReporter is null)
            return;

        var usage = isError || string.IsNullOrWhiteSpace(responseBody)
            ? new LlmUsage(_model, 0, 0, 0, 0, IsError: isError, DurationMs: durationMs)
            : BuildUsage(responseBody, _model, durationMs);
        _usageReporter.Report(usage);
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

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = await _http.PostAsync("/v1/messages", content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();

        if (!response.IsSuccessStatusCode)
        {
            ReportUsage(responseBody: null, stopwatch.ElapsedMilliseconds, isError: true);
            throw new HttpRequestException($"Anthropic API error {response.StatusCode}: {responseBody}");
        }

        ReportUsage(responseBody, stopwatch.ElapsedMilliseconds, isError: false);

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

    // ── Functional connectivity test (FR-010) ────────────────────────────────

    /// <summary>
    /// Sends a minimal inference request to confirm the stored API key and model are functional.
    /// Uses the same <c>HttpClient</c> and model as the live pipeline — not just a ping (FR-010).
    /// </summary>
    public async Task<ConnectorTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        ConnectorTestResult Fail(string message) =>
            new(ConnectorType.LLM, false, message, DateTimeOffset.UtcNow);

        if (string.IsNullOrEmpty(_model))
            return Fail("Connector is not configured — no model name stored.");

        try
        {
            // Smallest valid inference request: one user turn, 5 tokens max.
            var probe = new AnthropicRequest(
                _model, MaxTokens: 5,
                [new("user", "Respond with the word READY.")],
                System: null);

            var json = JsonSerializer.Serialize(probe, JsonOpts);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync("/v1/messages", content, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return (int)response.StatusCode switch
                {
                    401 => Fail("Authentication failed — the API key was rejected by the provider (401 Unauthorized). Check the key value and try again."),
                    403 => Fail("Access denied — the API key lacks permission to use this model (403 Forbidden)."),
                    404 => Fail($"Model '{_model}' not found or not available on this account (404)."),
                    429 => Fail("Request rate limit or quota exceeded (429). Check your Anthropic account quota."),
                    _   => Fail($"Unexpected response from Anthropic: {(int)response.StatusCode} {response.StatusCode}."),
                };
            }

            var apiResponse = JsonSerializer.Deserialize<AnthropicResponse>(body, JsonOpts);
            var text = apiResponse?.Content?.FirstOrDefault()?.Text;

            if (string.IsNullOrEmpty(text))
                return Fail("Unexpected response shape — the model returned an empty content array. Check the model name.");

            return new ConnectorTestResult(
                ConnectorType.LLM, true,
                $"Model '{_model}' responded to a test inference request.",
                DateTimeOffset.UtcNow);
        }
        catch (TaskCanceledException)
        {
            return Fail("Test timed out — check network connectivity to the LLM provider.");
        }
        catch (HttpRequestException ex)
        {
            return Fail($"Service unreachable — {ex.Message}");
        }
    }

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
        List<AnthropicContentBlock>? Content,
        string? Model = null,
        AnthropicUsage? Usage = null);

    // Anthropic Messages API usage block. input/output map via the snake_case policy; the cache fields
    // need explicit names because their JSON keys include "_input_" which the policy would not produce.
    private record AnthropicUsage(
        int InputTokens = 0,
        int OutputTokens = 0,
        [property: JsonPropertyName("cache_read_input_tokens")] int CacheReadTokens = 0,
        [property: JsonPropertyName("cache_creation_input_tokens")] int CacheCreationTokens = 0);

    private record AnthropicContentBlock(
        string Type,
        string? Text,
        string? Id = null,
        string? Name = null,
        JsonElement? Input = null);
}
