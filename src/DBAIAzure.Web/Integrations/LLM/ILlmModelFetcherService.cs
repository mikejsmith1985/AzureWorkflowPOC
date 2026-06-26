// Contract for fetching the live model list from an LLM provider.
namespace DBAIAzure.Web.Integrations.LLM;

/// <summary>
/// Makes a live API call to the selected LLM provider to return the current set of
/// available model identifiers. Results must never be cached — the caller always
/// receives the live list so newly released models appear immediately.
/// </summary>
public interface ILlmModelFetcherService
{
    /// <summary>
    /// Returns the sorted list of model IDs available for <paramref name="provider"/>
    /// using <paramref name="apiKey"/> for authentication.
    /// Throws <see cref="HttpRequestException"/> on network failure or bad credentials.
    /// </summary>
    /// <param name="provider">Provider identifier — "anthropic" or "openai".</param>
    /// <param name="apiKey">The API key to authenticate the models request.</param>
    Task<IReadOnlyList<string>> FetchModelsAsync(
        string provider, string apiKey, CancellationToken ct = default);
}
