// Unit tests for the per-run Work Tracking System config resolver (spec-020, T003).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

/// <summary>
/// Verifies that <see cref="WorkTrackerConfigResolver"/> resolves the active provider from the stored
/// connector's discriminated JSON, falls back to the seed value when unconfigured, and never throws on a
/// store failure — the guarantees the pipeline depends on for hot-reloadable tracker selection.
/// </summary>
public class WorkTrackerConfigResolverTests
{
    [Fact]
    public async Task ResolvesJira_FromProviderDiscriminator_WithDecryptedSecret()
    {
        var repo = new FakeConnectorConfigRepository
        {
            Config = new ConnectorConfig(
                ConnectorType.WorkTracker,
                NonSecretConfig: """{"provider":"Jira","siteUrl":"https://x.atlassian.net","email":"a@b.c","projectKey":"PROJ"}""",
                HasSecrets: true,
                IsConfigured: true,
                LastUpdatedAt: default,
                LastTestResult: null),
            DecryptedSecret = """{"apiToken":"tok"}""",
        };

        var resolved = await CreateResolver(repo).ResolveActiveAsync();

        Assert.True(resolved.IsConfigured);
        Assert.Equal(WorkTrackerProvider.Jira, resolved.Provider);
        Assert.Equal("""{"apiToken":"tok"}""", resolved.DecryptedSecret);
    }

    [Fact]
    public async Task ResolvesAzureDevOps_FromProviderDiscriminator()
    {
        var repo = new FakeConnectorConfigRepository
        {
            Config = new ConnectorConfig(
                ConnectorType.WorkTracker,
                NonSecretConfig: """{"provider":"AzureDevOps","organizationUrl":"https://dev.azure.com/o","projectName":"P"}""",
                HasSecrets: true,
                IsConfigured: true,
                LastUpdatedAt: default,
                LastTestResult: null),
        };

        var resolved = await CreateResolver(repo).ResolveActiveAsync();

        Assert.True(resolved.IsConfigured);
        Assert.Equal(WorkTrackerProvider.AzureDevOps, resolved.Provider);
    }

    [Fact]
    public async Task FallsBackToSeedProvider_WhenNoConnectorRow()
    {
        var resolver = CreateResolver(new FakeConnectorConfigRepository { Config = null }, seed: "Jira");

        var resolved = await resolver.ResolveActiveAsync();

        Assert.False(resolved.IsConfigured);
        Assert.Equal(WorkTrackerProvider.Jira, resolved.Provider);
    }

    [Fact]
    public async Task DefaultsToAzureDevOps_WhenNoRowAndNoSeed()
    {
        var resolved = await CreateResolver(new FakeConnectorConfigRepository { Config = null }).ResolveActiveAsync();

        Assert.False(resolved.IsConfigured);
        Assert.Equal(WorkTrackerProvider.AzureDevOps, resolved.Provider);
    }

    [Fact]
    public async Task ResolvesUnconfigured_WhenStoreThrows()
    {
        var resolved = await CreateResolver(new FakeConnectorConfigRepository { ThrowOnGet = true }).ResolveActiveAsync();

        Assert.False(resolved.IsConfigured);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WorkTrackerConfigResolver CreateResolver(FakeConnectorConfigRepository repo, string? seed = null)
    {
        var settings = seed is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?> { ["WorkTracker:Active"] = seed };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new WorkTrackerConfigResolver(repo, configuration, NullLogger<WorkTrackerConfigResolver>.Instance);
    }

    /// <summary>Hand-rolled fake — returns a preset connector and secret, or throws to exercise the guard.</summary>
    private sealed class FakeConnectorConfigRepository : IConnectorConfigRepository
    {
        public ConnectorConfig? Config { get; set; }
        public string? DecryptedSecret { get; set; }
        public bool ThrowOnGet { get; set; }

        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) =>
            ThrowOnGet ? throw new InvalidOperationException("store down") : Task.FromResult(Config);

        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult(DecryptedSecret);

        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConnectorConfig>>(Config is null ? [] : [Config]);

        public Task SaveAsync(ConnectorType type, string? nonSecretConfigJson, string? plaintextSecretsJson, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
