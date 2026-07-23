// The Jira webhook signing secret moved onto the Work Tracking System connector, next to the other Jira
// credentials. These tests pin the precedence: the work-tracker row wins, and installs configured before the
// move keep working from the legacy DoR row.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Services.Dor;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class DorWebhookSecretSourceTests
{
    [Fact]
    public async Task ResolveSecretsAsync_PrefersTheWebhookSecretOnTheWorkTrackerConnector()
    {
        var repo = new FakeSecretsRepo
        {
            DorSecrets = """{"jira_webhook_secret":"legacy"}""",
            WorkTrackerSecrets = """{"apiToken":"t","jiraWebhookSecret":"current"}""",
        };

        var secrets = await Build(repo).ResolveSecretsAsync();

        Assert.Equal("current", secrets.JiraWebhookSecret);
    }

    [Fact]
    public async Task ResolveSecretsAsync_FallsBackToTheLegacyDorRowWhenTheConnectorHasNoWebhookSecret()
    {
        // An install upgraded from the previous layout must keep validating webhooks until it is re-entered.
        var repo = new FakeSecretsRepo
        {
            DorSecrets = """{"jira_webhook_secret":"legacy"}""",
            WorkTrackerSecrets = """{"apiToken":"t"}""",
        };

        var secrets = await Build(repo).ResolveSecretsAsync();

        Assert.Equal("legacy", secrets.JiraWebhookSecret);
    }

    [Fact]
    public async Task ResolveSecretsAsync_KeepsTheOtherDorSecretsWhenTheConnectorSuppliesTheWebhookSecret()
    {
        var repo = new FakeSecretsRepo
        {
            DorSecrets = """{"jira_api_token":"api","slack_token":"slack","ai_api_key":"ai"}""",
            WorkTrackerSecrets = """{"jiraWebhookSecret":"current"}""",
        };

        var secrets = await Build(repo).ResolveSecretsAsync();

        Assert.Equal("current", secrets.JiraWebhookSecret);
        Assert.Equal("slack", secrets.SlackToken);
        Assert.Equal("ai", secrets.AiApiKey);
    }

    [Fact]
    public async Task ResolveSecretsAsync_WithNothingStored_ReportsNoWebhookSecretRatherThanThrowing()
    {
        var secrets = await Build(new FakeSecretsRepo()).ResolveSecretsAsync();

        Assert.Null(secrets.JiraWebhookSecret);
    }

    private static DorConfigResolver Build(FakeSecretsRepo repo) =>
        new(repo, NullLogger<DorConfigResolver>.Instance);

    /// <summary>Serves a decrypted secrets blob per connector type; everything else is unused here.</summary>
    private sealed class FakeSecretsRepo : IConnectorConfigRepository
    {
        public string? DorSecrets { get; set; }
        public string? WorkTrackerSecrets { get; set; }

        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult(type switch
            {
                ConnectorType.DorWorkflow => DorSecrets,
                ConnectorType.WorkTracker => WorkTrackerSecrets,
                _ => null,
            });

        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConnectorConfig>>([]);
        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<ConnectorConfig?>(null);
        public Task SaveAsync(ConnectorType type, string? nonSecretJson, string? secretsJson, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
