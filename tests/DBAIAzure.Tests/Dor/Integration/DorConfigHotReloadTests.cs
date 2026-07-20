// Verifies DoR config hot-reload (spec-021 T069 / FR-025/FR-026): the resolver reads the store per call so a
// saved change takes effect on the next resolution without a restart, and secrets never appear in the
// non-secret config. Uses a store fake that mirrors the real repository's save semantics.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Services.Dor;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Dor.Integration;

public sealed class DorConfigHotReloadTests
{
    private static string ConfigJson(string readyStatus) => $$"""
        { "jira": { "project_keys": ["SBRO"], "ready_transition_id": "31", "ready_status": "{{readyStatus}}" },
          "dor": { "source_type": "inline", "inline_markdown": "# DoR" } }
        """;

    private static string SecretJson(string apiToken) => $$"""{ "jira_api_token": "{{apiToken}}" }""";

    [Fact]
    public async Task SavedConfigChange_TakesEffectOnNextResolve_WithoutRestart()
    {
        var repo = new MutableConfigRepo();
        var resolver = new DorConfigResolver(repo, NullLogger<DorConfigResolver>.Instance);

        await repo.SaveAsync(ConnectorType.DorWorkflow, ConfigJson("Ready A"), SecretJson("token-A"));
        Assert.Equal("Ready A", (await resolver.ResolveActiveAsync()).Jira.ReadyStatus);

        // Change the config (secret unchanged — null leaves the existing secret) and resolve again.
        await repo.SaveAsync(ConnectorType.DorWorkflow, ConfigJson("Ready B"), plaintextSecretsJson: null);
        Assert.Equal("Ready B", (await resolver.ResolveActiveAsync()).Jira.ReadyStatus);   // hot-reload, no restart
    }

    [Fact]
    public async Task Secrets_AreResolvedSeparately_AndNeverInTheNonSecretConfig()
    {
        var repo = new MutableConfigRepo();
        var resolver = new DorConfigResolver(repo, NullLogger<DorConfigResolver>.Instance);
        await repo.SaveAsync(ConnectorType.DorWorkflow, ConfigJson("Ready"), SecretJson("super-secret-token"));

        var secrets = await resolver.ResolveSecretsAsync();
        Assert.Equal("super-secret-token", secrets.JiraApiToken);

        var stored = await repo.GetAsync(ConnectorType.DorWorkflow);
        Assert.DoesNotContain("super-secret-token", stored!.NonSecretConfig);   // secret never in the non-secret blob
    }

    // Mirrors the real repository's save semantics: a null plaintext-secrets argument leaves the existing secret.
    private sealed class MutableConfigRepo : IConnectorConfigRepository
    {
        private string? _nonSecret;
        private string? _secret;

        public Task SaveAsync(ConnectorType type, string? nonSecretConfigJson, string? plaintextSecretsJson, CancellationToken ct = default)
        {
            if (nonSecretConfigJson is not null) _nonSecret = nonSecretConfigJson;
            if (plaintextSecretsJson is not null) _secret = plaintextSecretsJson;
            return Task.CompletedTask;
        }

        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<ConnectorConfig?>(_nonSecret is null
                ? null
                : new ConnectorConfig(type, _nonSecret, _secret is not null, true, DateTimeOffset.UtcNow, null));

        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) => Task.FromResult(_secret);

        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConnectorConfig>>(Array.Empty<ConnectorConfig>());
        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) => Task.CompletedTask;
    }
}
