// bUnit tests for the generic Work Tracking System card's Jira sub-form (spec-020, T019).
using Bunit;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Web.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

/// <summary>
/// Renders the ConnectorSettings page for a Jira-provider Work Tracking System connector and asserts the
/// generic card shows the Jira fields and never pre-fills the API token on reload (SC-005/FR-006).
/// </summary>
public sealed class ConnectorSettingsJiraFormTests : TestContext
{
    [Fact]
    public void WorkTrackerCard_WithJiraProvider_RendersJiraFields_AndBlankToken()
    {
        var jiraConfig = new ConnectorConfig(
            ConnectorType.WorkTracker,
            NonSecretConfig: """{"provider":"Jira","siteUrl":"https://x.atlassian.net","email":"a@b.c","projectKey":"PROJ"}""",
            HasSecrets: true, IsConfigured: true, LastUpdatedAt: DateTimeOffset.UtcNow, LastTestResult: null);
        RegisterServices(new FakeRepo([jiraConfig]));

        var cut = RenderComponent<ConnectorSettings>();
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Edit").Click();

        // The provider selector renders and the Jira sub-form fields are present.
        Assert.NotEmpty(cut.FindAll("[data-testid='worktracker-provider']"));
        Assert.Contains("Site URL", cut.Markup);
        Assert.Contains("Account Email", cut.Markup);
        Assert.Contains("Project Key", cut.Markup);

        // The API token password input is never pre-filled from stored secrets (Article IX / SC-005).
        var passwordInputs = cut.FindAll("input[type=password]");
        Assert.All(passwordInputs, input => Assert.True(string.IsNullOrEmpty(input.GetAttribute("value"))));
    }

    [Fact]
    public void WorkTrackerCard_Unconfigured_ShowsEmptyState()
    {
        var unconfigured = new ConnectorConfig(
            ConnectorType.WorkTracker, NonSecretConfig: null, HasSecrets: false,
            IsConfigured: false, LastUpdatedAt: default, LastTestResult: null);
        RegisterServices(new FakeRepo([unconfigured]));

        var cut = RenderComponent<ConnectorSettings>();
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Edit").Click();

        // No provider chosen yet → the empty-state prompt is shown (spec edge case).
        Assert.NotEmpty(cut.FindAll("[data-testid='worktracker-empty-state']"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RegisterServices(FakeRepo repo)
    {
        Services.AddSingleton<IConnectorConfigRepository>(repo);
        Services.AddSingleton<IConnectorHealthChecker>(new NullHealthChecker());
        Services.AddSingleton<IAdoTelemetryPreflightService>(new NullPreflight());
        Services.AddSingleton<DBAIAzure.Web.Integrations.LLM.ILlmModelFetcherService>(new NullModelFetcher());
        // The InfoTip components in the edit form inject ITooltipService.
        Services.AddSingleton<DBAIAzure.Web.Services.ITooltipService, DBAIAzure.Web.Services.TooltipService>();
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<ConnectorSettings>),
            NullLogger<ConnectorSettings>.Instance);
    }

    private sealed class FakeRepo : IConnectorConfigRepository
    {
        private readonly IReadOnlyList<ConnectorConfig> _configs;
        public FakeRepo(IReadOnlyList<ConnectorConfig> configs) => _configs = configs;
        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) => Task.FromResult(_configs);
        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) => Task.FromResult<ConnectorConfig?>(null);
        public Task SaveAsync(ConnectorType type, string? n, string? s, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NullHealthChecker : IConnectorHealthChecker
    {
        public Task<ConnectorTestResult> TestAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult(new ConnectorTestResult(type, true, "ok", DateTimeOffset.UtcNow));
        public Task<IReadOnlyList<ConnectorTestResult>> CheckAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConnectorTestResult>>([]);
    }

    private sealed class NullPreflight : IAdoTelemetryPreflightService
    {
        public Task<PreflightResult> RunPreflightAsync(AdoTelemetryFieldConfig? overrideConfig, CancellationToken ct = default) =>
            Task.FromResult(PreflightResult.Fail("n/a"));
    }

    private sealed class NullModelFetcher : DBAIAzure.Web.Integrations.LLM.ILlmModelFetcherService
    {
        public Task<IReadOnlyList<string>> FetchModelsAsync(string provider, string apiKey, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }
}
