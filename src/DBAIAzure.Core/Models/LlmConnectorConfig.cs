// Non-secret configuration fields for the LLM provider connector.
namespace DBAIAzure.Core.Models;

/// <summary>
/// Non-secret fields for the LLM (language model) connector, serialized to/from
/// <c>ConnectorConfig.NonSecretConfig</c>. The API key is stored in the encrypted secrets blob.
/// </summary>
public record LlmConnectorConfig(
    /// <summary>Provider identifier — "anthropic" or "openai".</summary>
    string Provider,

    /// <summary>Model identifier resolved from the provider's live model list.</summary>
    string ModelName);
