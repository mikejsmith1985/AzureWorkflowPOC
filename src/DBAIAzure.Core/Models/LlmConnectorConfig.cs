// Non-secret configuration fields for the LLM provider connector.
namespace DBAIAzure.Core.Models;

/// <summary>
/// Non-secret fields for the LLM (language model) connector, serialized to/from
/// <c>ConnectorConfig.NonSecretConfig</c>. The API key is stored in the encrypted secrets blob.
/// </summary>
public record LlmConnectorConfig(
    /// <summary>Base URL of the LLM provider (e.g., <c>https://api.anthropic.com</c>).</summary>
    string ProviderEndpoint,

    /// <summary>Model identifier used in inference requests (e.g., <c>claude-sonnet-4-6</c>).</summary>
    string ModelName);
