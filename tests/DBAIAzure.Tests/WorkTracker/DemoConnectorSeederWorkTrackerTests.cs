// Tests the demo connector seeder's generic Work Tracking System path (spec-020) — Jira + legacy-ADO mapping.
using System.Text.Json;
using DBAIAzure.Core.Configuration;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

/// <summary>
/// Verifies the seeder writes the generic WorkTracker connector with the correct discriminated JSON for Jira,
/// maps the legacy AzureDevOps seed section onto provider = Azure DevOps, and honours the Overwrite flag.
/// </summary>
public sealed class DemoConnectorSeederWorkTrackerTests
{
    [Fact]
    public async Task SeedsJira_WithDiscriminatedJson_AndApiTokenSecret()
    {
        var repo = new CapturingRepo();
        var seeder = Build(repo, new WorkTrackerSeed
        {
            Provider = "Jira", SiteUrl = "https://x.atlassian.net", Email = "a@b.c",
            ProjectKey = "PROJ", ApiToken = "tok",
        });

        await seeder.SeedAsync();

        var saved = repo.Saved.Single(s => s.Type == ConnectorType.WorkTracker);
        using var nonSecret = JsonDocument.Parse(saved.NonSecret!);
        Assert.Equal("Jira", nonSecret.RootElement.GetProperty("provider").GetString());
        Assert.Equal("https://x.atlassian.net", nonSecret.RootElement.GetProperty("siteUrl").GetString());
        using var secret = JsonDocument.Parse(saved.Secret!);
        Assert.Equal("tok", secret.RootElement.GetProperty("apiToken").GetString());
    }

    [Fact]
    public async Task MapsLegacyAzureDevOpsSection_ToProviderAzureDevOps()
    {
        var repo = new CapturingRepo();
        var options = new ConnectorSeedOptions
        {
            AzureDevOps = new AzureDevOpsSeed { OrganizationUrl = "https://dev.azure.com/o", ProjectName = "P", PersonalAccessToken = "pat" },
        };
        var seeder = new DemoConnectorSeeder(repo, Options.Create(options), NullLogger<DemoConnectorSeeder>.Instance);

        await seeder.SeedAsync();

        var saved = repo.Saved.Single(s => s.Type == ConnectorType.WorkTracker);
        using var nonSecret = JsonDocument.Parse(saved.NonSecret!);
        Assert.Equal("AzureDevOps", nonSecret.RootElement.GetProperty("provider").GetString());
    }

    [Fact]
    public async Task SkipsWhenAlreadyConfigured_UnlessOverwrite()
    {
        var repo = new CapturingRepo { AlreadyConfigured = true };
        static WorkTrackerSeed Jira(bool overwrite) => new()
        {
            Provider = "Jira", SiteUrl = "https://x", Email = "e", ProjectKey = "P", ApiToken = "t", Overwrite = overwrite,
        };

        await Build(repo, Jira(overwrite: false)).SeedAsync();
        Assert.DoesNotContain(repo.Saved, s => s.Type == ConnectorType.WorkTracker);

        await Build(repo, Jira(overwrite: true)).SeedAsync();
        Assert.Contains(repo.Saved, s => s.Type == ConnectorType.WorkTracker);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DemoConnectorSeeder Build(CapturingRepo repo, WorkTrackerSeed workTracker) =>
        new(repo, Options.Create(new ConnectorSeedOptions { WorkTracker = workTracker }), NullLogger<DemoConnectorSeeder>.Instance);

    private sealed class CapturingRepo : IConnectorConfigRepository
    {
        public List<(ConnectorType Type, string? NonSecret, string? Secret)> Saved { get; } = [];
        public bool AlreadyConfigured { get; set; }

        public Task SaveAsync(ConnectorType type, string? nonSecretConfigJson, string? plaintextSecretsJson, CancellationToken ct = default)
        {
            Saved.Add((type, nonSecretConfigJson, plaintextSecretsJson));
            return Task.CompletedTask;
        }
        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<ConnectorConfig?>(AlreadyConfigured
                ? new ConnectorConfig(type, "{}", true, true, default, null)
                : null);
        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConnectorConfig>>([]);
        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
