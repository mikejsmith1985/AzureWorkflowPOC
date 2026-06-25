// Proves the design-time LLM service (Workflow Builder chat / Node Realization) resolves the visitor's
// entered key+model from the LLM connector's DB row when present, and falls back to configuration when
// absent — i.e. the single user-supplied key reaches design-time features with no restart
// (research Decision 7; FR-003 / FR-004 / SC-006). Tests the resolution rule directly, no network call.
using DBAIAzure.Connectors;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class DesignTimeLlmKeyResolutionTests
{
    private const string FallbackKey = "config-fallback-key";
    private const string FallbackModel = "config-fallback-model";

    [Fact]
    public async Task ResolveCredentials_UsesDbValues_WhenLlmConnectorConfigured()
    {
        var repo = new StubRepository
        {
            NonSecretConfig = """{"provider":"anthropic","modelName":"claude-from-db"}""",
            DecryptedSecrets = """{"apiKey":"key-from-db"}""",
        };
        var service = new HotReloadAnthropicService(repo, FallbackKey, FallbackModel);

        var (apiKey, model) = await service.ResolveCredentialsAsync(CancellationToken.None);

        Assert.Equal("key-from-db", apiKey);
        Assert.Equal("claude-from-db", model);
    }

    [Fact]
    public async Task ResolveCredentials_FallsBackToConfig_WhenNoLlmConfig()
    {
        var service = new HotReloadAnthropicService(new StubRepository(), FallbackKey, FallbackModel);

        var (apiKey, model) = await service.ResolveCredentialsAsync(CancellationToken.None);

        Assert.Equal(FallbackKey, apiKey);
        Assert.Equal(FallbackModel, model);
    }

    [Fact]
    public async Task ResolveCredentials_UsesDbModelButFallbackKey_WhenOnlyModelStored()
    {
        var repo = new StubRepository { NonSecretConfig = """{"modelName":"claude-from-db"}""" };
        var service = new HotReloadAnthropicService(repo, FallbackKey, FallbackModel);

        var (apiKey, model) = await service.ResolveCredentialsAsync(CancellationToken.None);

        Assert.Equal(FallbackKey, apiKey);
        Assert.Equal("claude-from-db", model);
    }

    /// <summary>Returns canned LLM connector config/secrets; only the two read methods are exercised.</summary>
    private sealed class StubRepository : IConnectorConfigRepository
    {
        public string? NonSecretConfig { get; init; }
        public string? DecryptedSecrets { get; init; }

        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<ConnectorConfig?>(
                NonSecretConfig is null
                    ? null
                    : new ConnectorConfig(type, NonSecretConfig, DecryptedSecrets is not null, true, DateTimeOffset.UtcNow, null));

        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult(DecryptedSecrets);

        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConnectorConfig>>(Array.Empty<ConnectorConfig>());

        public Task SaveAsync(ConnectorType type, string? nonSecretConfigJson, string? plaintextSecretsJson, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
