// bUnit tests for ConnectorSettings.razor: row rendering and edit-save flow (T063).
//
// NOTE: The task spec originally requested a Delete button calling IConnectorConfigRepository.DeleteAsync,
// but IConnectorConfigRepository (one config per ConnectorType) has no DeleteAsync — deletion is not
// meaningful when the model is a single-entry-per-type configuration. These tests cover the actual
// implemented contract: render-one-row-per-ConnectorConfig and the edit/save cycle.
using Bunit;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Web.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// bUnit component tests for <see cref="ConnectorSettings"/>.
/// Verifies that the page renders one card per <see cref="ConnectorConfig"/> returned by the
/// repository (T063) and that the edit/save cycle routes through <see cref="IConnectorConfigRepository.SaveAsync"/>.
/// </summary>
public sealed class ConnectorSettingsPanelTests : TestContext
{
    // ── T063: renders one card per ConnectorConfig ───────────────────────────

    [Fact]
    public async Task ConnectorSettings_RendersOneCardPerConfig()
    {
        var configs = new List<ConnectorConfig>
        {
            MakeConfig(ConnectorType.AzureDevOps, isConfigured: true),
            MakeConfig(ConnectorType.LLM,         isConfigured: false),
            MakeConfig(ConnectorType.Messaging,        isConfigured: true),
        };

        var repo         = new FakeConfigRepo(configs);
        var healthChecker = new NullHealthChecker();
        RegisterServices(repo, healthChecker);

        var cut = RenderComponent<ConnectorSettings>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        // Three Edit buttons — one per connector card. Select by text rather than the colour class,
        // which is now a semantic token after the spec-014 restyle.
        var editButtons = cut.FindAll("button").Where(b => b.TextContent.Trim() == "Edit").ToList();
        Assert.Equal(3, editButtons.Count);
    }

    [Fact]
    public async Task ConnectorSettings_ShowsConfiguredBadgeForConfiguredConnectors()
    {
        var configs = new List<ConnectorConfig>
        {
            MakeConfig(ConnectorType.AzureDevOps, isConfigured: true),
            MakeConfig(ConnectorType.LLM,         isConfigured: false),
        };

        var repo = new FakeConfigRepo(configs);
        RegisterServices(repo, new NullHealthChecker());

        var cut = RenderComponent<ConnectorSettings>();
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Contains("Configured",   cut.Markup);
        Assert.Contains("Not configured", cut.Markup);
    }

    [Fact]
    public async Task ConnectorSettings_WhenSaveClicked_CallsRepositorySaveAsync()
    {
        var configs = new List<ConnectorConfig> { MakeConfig(ConnectorType.LLM, isConfigured: false) };
        var repo    = new FakeConfigRepo(configs);
        RegisterServices(repo, new NullHealthChecker());

        var cut = RenderComponent<ConnectorSettings>();

        // Open the edit panel — bUnit re-renders synchronously after .Click().
        cut.FindAll("button").First(b => b.TextContent == "Edit").Click();

        // Click Save — bUnit awaits the async Task returned by the handler before returning.
        cut.FindAll("button").First(b => b.TextContent == "Save").Click();

        // Allow the async SaveEntry to flush through the component's dispatcher.
        await cut.InvokeAsync(() => Task.CompletedTask);

        Assert.Equal(1, repo.SaveCallCount);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void RegisterServices(FakeConfigRepo repo, IConnectorHealthChecker checker)
    {
        Services.AddSingleton<IConnectorConfigRepository>(repo);
        Services.AddSingleton(checker);
        Services.AddSingleton<IAdoTelemetryPreflightService>(new NullPreflightService());
        Services.AddSingleton<DBAIAzure.Web.Integrations.LLM.ILlmModelFetcherService>(new NullLlmModelFetcher());
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<ConnectorSettings>),
            NullLogger<ConnectorSettings>.Instance);
    }

    private static ConnectorConfig MakeConfig(ConnectorType type, bool isConfigured) =>
        new ConnectorConfig(
            Type:            type,
            NonSecretConfig: isConfigured ? "{}" : null,
            HasSecrets:      isConfigured,
            IsConfigured:    isConfigured,
            LastUpdatedAt:   DateTimeOffset.UtcNow,
            LastTestResult:  null);

    // ── Fakes ────────────────────────────────────────────────────────────────

    private sealed class FakeConfigRepo : IConnectorConfigRepository
    {
        private readonly IReadOnlyList<ConnectorConfig> _configs;
        public int SaveCallCount { get; private set; }

        public FakeConfigRepo(IReadOnlyList<ConnectorConfig> configs) => _configs = configs;

        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult(_configs);
        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<ConnectorConfig?>(null);
        public Task SaveAsync(ConnectorType type, string? nonSecretJson, string? secretsJson, CancellationToken ct = default)
        {
            SaveCallCount++;
            return Task.CompletedTask;
        }
        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);
        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NullLlmModelFetcher : DBAIAzure.Web.Integrations.LLM.ILlmModelFetcherService
    {
        public Task<IReadOnlyList<string>> FetchModelsAsync(string provider, string apiKey, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private sealed class NullPreflightService : IAdoTelemetryPreflightService
    {
        public Task<PreflightResult> RunPreflightAsync(AdoTelemetryFieldConfig? _, CancellationToken __)
            => Task.FromResult(PreflightResult.Succeed(new BootstrapManifest
            {
                Timestamp = DateTimeOffset.UtcNow,
                OrgUrl = "https://dev.azure.com/test",
                Project = "Test",
                ProcessType = AdoProcessType.Agile,
                FieldsCreated = Array.Empty<string>(),
                FieldsExisting = Array.Empty<string>(),
                FieldsFailed = Array.Empty<FieldBootstrapFailure>(),
            }));
    }

    private sealed class NullHealthChecker : IConnectorHealthChecker
    {
        public Task<ConnectorTestResult> TestAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult(new ConnectorTestResult(type, true, "OK", DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<ConnectorTestResult>> CheckAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConnectorTestResult>>([]);
    }
}
