// Unit tests for DemoConnectorSeeder: proves it seeds the three back-office connectors with the exact
// JSON shapes the Settings UI uses, never seeds the LLM connector, tolerates missing values per
// connector, skips already-configured connectors, and never logs a secret value.
using System.Text.Json;
using DBAIAzure.Core.Configuration;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class DemoConnectorSeederTests
{
    private const string ServiceNowUrl = "https://dev12345.service-now.com";
    private const string ServiceNowUser = "integration.user";
    private const string ServiceNowPassword = "sn-secret-pw";
    private const string AdoOrgUrl = "https://dev.azure.com/contoso";
    private const string AdoProject = "Platform";
    private const string AdoPat = "ado-secret-pat";
    private const string Webhook = "https://hooks.slack.com/services/AAA/BBB/CCC";
    private const string McpToken = "mcp-secret-token";

    [Fact]
    public async Task SeedAsync_FullConfig_SeedsAllThreeConnectorsWithUiJsonShapes()
    {
        var repo = new RecordingRepository();
        var seeder = BuildSeeder(repo, FullOptions());

        await seeder.SeedAsync();

        Assert.Equal(3, repo.Saves.Count);

        var serviceNow = repo.SaveFor(ConnectorType.ServiceNow);
        Assert.Equal($$"""{"instanceUrl":"{{ServiceNowUrl}}","username":"{{ServiceNowUser}}"}""", serviceNow.NonSecret);
        Assert.Equal($$"""{"password":"{{ServiceNowPassword}}"}""", serviceNow.Secret);

        var ado = repo.SaveFor(ConnectorType.AzureDevOps);
        Assert.Equal($$"""{"organizationUrl":"{{AdoOrgUrl}}","projectName":"{{AdoProject}}"}""", ado.NonSecret);
        Assert.Equal($$"""{"personalAccessToken":"{{AdoPat}}"}""", ado.Secret);

        var messaging = repo.SaveFor(ConnectorType.Messaging);
        using var nonSecret = JsonDocument.Parse(messaging.NonSecret!);
        Assert.Equal("Slack", nonSecret.RootElement.GetProperty("platform").GetString());
    }

    [Fact]
    public async Task SeedAsync_NeverSeedsLlmConnector()
    {
        var repo = new RecordingRepository();
        var seeder = BuildSeeder(repo, FullOptions());

        await seeder.SeedAsync();

        // ConnectorSeedOptions has no LLM member, so seeding the LLM connector is structurally impossible.
        Assert.DoesNotContain(repo.Saves, save => save.Type == ConnectorType.LLM);
    }

    [Fact]
    public async Task SeedAsync_MissingServiceNowSecret_SkipsOnlyServiceNow()
    {
        var options = FullOptions();
        options.ServiceNow.Password = "   "; // blank secret
        var repo = new RecordingRepository();

        await BuildSeeder(repo, options).SeedAsync();

        Assert.DoesNotContain(repo.Saves, save => save.Type == ConnectorType.ServiceNow);
        Assert.Contains(repo.Saves, save => save.Type == ConnectorType.AzureDevOps);
        Assert.Contains(repo.Saves, save => save.Type == ConnectorType.Messaging);
    }

    [Fact]
    public async Task SeedAsync_UnknownMessagingPlatform_SkipsMessaging()
    {
        var options = FullOptions();
        options.Messaging.Platform = "Carrier Pigeon";
        var repo = new RecordingRepository();

        await BuildSeeder(repo, options).SeedAsync();

        Assert.DoesNotContain(repo.Saves, save => save.Type == ConnectorType.Messaging);
    }

    [Fact]
    public async Task SeedAsync_AlreadyConfiguredConnector_IsLeftUntouched()
    {
        var repo = new RecordingRepository();
        repo.Preconfigure(ConnectorType.ServiceNow);

        await BuildSeeder(repo, FullOptions()).SeedAsync();

        Assert.DoesNotContain(repo.Saves, save => save.Type == ConnectorType.ServiceNow);
    }

    [Fact]
    public async Task SeedAsync_NeverLogsSecretValues()
    {
        var repo = new RecordingRepository();
        var logger = new CapturingLogger();
        var seeder = new DemoConnectorSeeder(repo, Options.Create(FullOptions()), logger);

        await seeder.SeedAsync();

        var log = string.Join("\n", logger.Messages);
        Assert.DoesNotContain(ServiceNowPassword, log);
        Assert.DoesNotContain(AdoPat, log);
        Assert.DoesNotContain(Webhook, log);
        Assert.DoesNotContain(McpToken, log);
    }

    private static DemoConnectorSeeder BuildSeeder(IConnectorConfigRepository repo, ConnectorSeedOptions options) =>
        new(repo, Options.Create(options), new CapturingLogger());

    private static ConnectorSeedOptions FullOptions() => new()
    {
        ServiceNow = new ServiceNowSeed { InstanceUrl = ServiceNowUrl, Username = ServiceNowUser, Password = ServiceNowPassword },
        AzureDevOps = new AzureDevOpsSeed { OrganizationUrl = AdoOrgUrl, ProjectName = AdoProject, PersonalAccessToken = AdoPat },
        Messaging = new MessagingSeed { Platform = "slack", WebhookUrl = Webhook, McpAuthToken = McpToken },
    };

    /// <summary>Captures SaveAsync calls and can preconfigure connectors for the "already configured" path.</summary>
    private sealed class RecordingRepository : IConnectorConfigRepository
    {
        private readonly HashSet<ConnectorType> _configured = new();

        public List<(ConnectorType Type, string? NonSecret, string? Secret)> Saves { get; } = new();

        public (ConnectorType Type, string? NonSecret, string? Secret) SaveFor(ConnectorType type) =>
            Saves.Single(save => save.Type == type);

        public void Preconfigure(ConnectorType type) => _configured.Add(type);

        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default)
        {
            ConnectorConfig? config = _configured.Contains(type)
                ? new ConnectorConfig(type, "{}", true, true, DateTimeOffset.UtcNow, null)
                : null;
            return Task.FromResult(config);
        }

        public Task SaveAsync(ConnectorType type, string? nonSecretConfigJson, string? plaintextSecretsJson, CancellationToken ct = default)
        {
            Saves.Add((type, nonSecretConfigJson, plaintextSecretsJson));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConnectorConfig>>(Array.Empty<ConnectorConfig>());

        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    /// <summary>Minimal logger that records the formatted message of each log call for assertions.</summary>
    private sealed class CapturingLogger : ILogger<DemoConnectorSeeder>
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
