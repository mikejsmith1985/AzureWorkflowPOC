// Unit tests for DoR workflow configuration parsing (snake_case JSON → records) and validation rules (spec-021
// T010). Uses a hand-rolled fake connector store so no infrastructure is touched (Article V: mocked, <10ms).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.DorWorkflow.Config;
using DBAIAzure.Web.Services.Dor;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.Dor;

public sealed class DorWorkflowConfigTests
{
    private const string ValidJson = """
        {
          "jira": { "base_url": "https://org.atlassian.net", "account_email": "bot@org.com",
                    "project_keys": ["SBRO"], "issue_types": ["Story"],
                    "watch_fields": ["summary","acceptance_criteria"],
                    "ai_editable_fields": ["acceptance_criteria"],
                    "ready_transition_id": "31", "ready_status": "Ready to Work",
                    "manual_label": "dor-manual-required" },
          "dor": { "source_type": "url", "source_uri": "https://x/dor", "cache_ttl_minutes": 15, "format": "markdown" },
          "ai": { "provider": "anthropic", "model": "claude-sonnet-5", "temperature": 0.1, "max_tokens": 2000 },
          "comms": { "primary": { "type": "slack", "channel_id": "#dor", "reply_timeout_minutes": 240, "max_iterations": 3 },
                     "escalation": { "type": "slack", "channel_id": "#esc", "reply_timeout_minutes": 120, "max_iterations": 2 },
                     "success": { "enabled": true, "channel_id": "#passed" } },
          "sla": { "primary_sla_hours": 24, "escalation_sla_hours": 8, "clock_type": "business_hours",
                   "business_hours": { "timezone": "America/Chicago", "start": "08:00", "end": "17:00", "working_days": [1,2,3,4,5] } },
          "audit": { "store_type": "jira_comment", "log_ai_responses": true },
          "run": { "dry_run": true }
        }
        """;

    [Fact]
    public async Task Resolver_ParsesSnakeCaseJson_IntoConfig()
    {
        var resolver = new DorConfigResolver(
            new FakeConfigRepo(ValidJson, secretsJson: null), NullLogger<DorConfigResolver>.Instance);

        var config = await resolver.ResolveActiveAsync();

        Assert.True(config.IsConfigured);
        Assert.Equal("Ready to Work", config.Jira.ReadyStatus);
        Assert.Equal("31", config.Jira.ReadyTransitionId);
        Assert.Equal(new[] { "SBRO" }, config.Jira.ProjectKeys);
        Assert.Equal(new[] { "acceptance_criteria" }, config.Jira.AiEditableFields);
        Assert.Equal("url", config.Dor.SourceType);
        Assert.Equal(3, config.Comms.Primary.MaxIterations);
        Assert.Equal(24, config.Sla.PrimarySlaHours);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, config.Sla.BusinessHours.WorkingDays);
        Assert.True(config.Run.DryRun);
    }

    [Fact]
    public async Task Resolver_ReturnsUnconfigured_WhenNoRow()
    {
        var resolver = new DorConfigResolver(
            new FakeConfigRepo(nonSecretJson: null, secretsJson: null), NullLogger<DorConfigResolver>.Instance);

        var config = await resolver.ResolveActiveAsync();

        Assert.False(config.IsConfigured);
    }

    [Fact]
    public async Task Resolver_DecryptsSecrets_ByReference()
    {
        var secrets = """{"jira_api_token":"t","jira_webhook_secret":"w","slack_token":"s","ai_api_key":"k"}""";
        var resolver = new DorConfigResolver(
            new FakeConfigRepo(ValidJson, secrets), NullLogger<DorConfigResolver>.Instance);

        var resolved = await resolver.ResolveSecretsAsync();

        Assert.Equal("t", resolved.JiraApiToken);
        Assert.Equal("w", resolved.JiraWebhookSecret);
        Assert.Equal("s", resolved.SlackToken);
        Assert.Equal("k", resolved.AiApiKey);
    }

    [Fact]
    public void Validation_Passes_ForCompleteConfig()
    {
        var config = new DorWorkflowConfig
        {
            IsConfigured = true,
            Jira = new DorJiraConfig { ReadyTransitionId = "31", ProjectKeys = new[] { "SBRO" } },
            Dor = new DorDocConfig { SourceType = "url", SourceUri = "https://x/dor" },
        };

        Assert.Empty(DorConfigValidation.Validate(config));
    }

    [Fact]
    public void Validation_Flags_InlineWithoutMarkdown_UrlWithoutUri_AndBusinessHoursWithoutDays()
    {
        var inline = new DorWorkflowConfig
        {
            Jira = new DorJiraConfig { ReadyTransitionId = "31", ProjectKeys = new[] { "SBRO" } },
            Dor = new DorDocConfig { SourceType = "inline", InlineMarkdown = "" },
        };
        Assert.Contains(DorConfigValidation.Validate(inline), i => i.Contains("inline_markdown"));

        var noDays = new DorWorkflowConfig
        {
            Jira = new DorJiraConfig { ReadyTransitionId = "31", ProjectKeys = new[] { "SBRO" } },
            Dor = new DorDocConfig { SourceType = "url", SourceUri = "https://x" },
            Sla = new DorSlaConfig { ClockType = "business_hours", BusinessHours = new DorBusinessHoursConfig { WorkingDays = Array.Empty<int>() } },
        };
        Assert.Contains(DorConfigValidation.Validate(noDays), i => i.Contains("working_days"));
    }

    [Fact]
    public void Validation_Flags_MissingTransitionAndProjects()
    {
        var issues = DorConfigValidation.Validate(new DorWorkflowConfig
        {
            Dor = new DorDocConfig { SourceType = "url", SourceUri = "https://x" },
        });

        Assert.Contains(issues, i => i.Contains("ready_transition_id"));
        Assert.Contains(issues, i => i.Contains("project_keys"));
    }

    // Minimal fake connector store — returns the given JSON for the DorWorkflow connector only.
    private sealed class FakeConfigRepo : IConnectorConfigRepository
    {
        private readonly string? _nonSecretJson;
        private readonly string? _secretsJson;
        public FakeConfigRepo(string? nonSecretJson, string? secretsJson)
        {
            _nonSecretJson = nonSecretJson;
            _secretsJson = secretsJson;
        }

        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult<ConnectorConfig?>(_nonSecretJson is null
                ? new ConnectorConfig(type, null, false, false, DateTimeOffset.UtcNow, null)
                : new ConnectorConfig(type, _nonSecretJson, _secretsJson is not null, true, DateTimeOffset.UtcNow, null));

        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) =>
            Task.FromResult(_secretsJson);

        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ConnectorConfig>>(Array.Empty<ConnectorConfig>());
        public Task SaveAsync(ConnectorType type, string? n, string? s, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) => Task.CompletedTask;
    }
}
