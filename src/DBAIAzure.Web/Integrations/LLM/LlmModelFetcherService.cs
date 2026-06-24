// Live model-list fetcher — queries the provider's API each time, no caching.
using System.Net.Http.Headers;
using System.Text.Json;

namespace DBAIAzure.Web.Integrations.LLM;

/// <summary>
/// Fetches the live model list from Anthropic or OpenAI using the caller-supplied API key.
/// No hardcoded model names and no fallback lists — if the call fails, the exception propagates
/// so the UI can show the real error to the user.
/// </summary>
public sealed class LlmModelFetcherService : ILlmModelFetcherService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public LlmModelFetcherService(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> FetchModelsAsync(
        string provider, string apiKey, CancellationToken ct = default)
    {
        var http = _httpClientFactory.CreateClient(nameof(LlmModelFetcherService));
        return provider.ToLowerInvariant() switch
        {
            "anthropic" => FetchAnthropicModelsAsync(http, apiKey, ct),
            "openai"    => FetchOpenAiModelsAsync(http, apiKey, ct),
            _           => throw new NotSupportedException($"Unknown provider: '{provider}'."),
        };
    }

    /// <summary>
    /// GET https://api.anthropic.com/v1/models — returns {"data":[{"id":"claude-..."},...]}
    /// </summary>
    private static async Task<IReadOnlyList<string>> FetchAnthropicModelsAsync(
        HttpClient http, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "https://api.anthropic.com/v1/models?limit=1000");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp))
                {
                    var id = idProp.GetString();
                    if (!string.IsNullOrEmpty(id)) models.Add(id);
                }
            }
        }
        return models.Order().ToList();
    }

    /// <summary>
    /// GET https://api.openai.com/v1/models — returns {"data":[{"id":"gpt-4o"},...]}
    /// </summary>
    private static async Task<IReadOnlyList<string>> FetchOpenAiModelsAsync(
        HttpClient http, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, "https://api.openai.com/v1/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);

        var models = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data))
        {
            foreach (var item in data.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp))
                {
                    var id = idProp.GetString();
                    if (!string.IsNullOrEmpty(id)) models.Add(id);
                }
            }
        }
        return models.Order().ToList();
    }
}
