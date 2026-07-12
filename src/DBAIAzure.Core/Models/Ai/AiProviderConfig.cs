// The immutable selection of an AI provider + model for a deployment instance (spec-019 BYO-AI):
// which provider is active, which model, and the API key resolved by reference from configuration.
namespace DBAIAzure.Core.Models.Ai;

/// <summary>
/// A single active AI provider/model selection for the deployment (spec-019 "bring your own AI").
/// One active configuration applies per instance; the API key is resolved by reference from
/// configuration/secrets and never hard-coded (Constitution Article IX). Value equality lets the
/// hot-reloading client detect when the resolved provider/model/key has changed.
/// </summary>
/// <param name="ProviderId">The active provider id (e.g. "anthropic"), matched case-insensitively.</param>
/// <param name="Model">The model id to use for this provider (e.g. a Claude model).</param>
/// <param name="ApiKey">The provider API key, resolved at runtime from configuration/secrets.</param>
/// <param name="MaxOutputTokens">Optional per-call output token ceiling; provider default when null.</param>
public sealed record AiProviderConfig(
    string ProviderId,
    string Model,
    string ApiKey,
    int? MaxOutputTokens = null)
{
    /// <summary>The provider used when configuration does not name one — the product's default.</summary>
    public const string DefaultProviderId = "anthropic";
}
