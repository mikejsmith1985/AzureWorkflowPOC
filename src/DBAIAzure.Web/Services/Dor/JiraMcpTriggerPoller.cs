// MCP-first trigger for the DoR workflow: asks the Jira MCP server for newly-created tickets on an interval and
// starts a run for each one. MCP is request/response — a server cannot call into us — so "MCP-first trigger"
// means polling. The inbound HMAC webhook remains available as the lower-latency fallback path.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Web.Integrations.Jira;

namespace DBAIAzure.Web.Services.Dor;

/// <summary>
/// Background service that polls the Jira MCP search tool for tickets created since the last sweep and starts the
/// DoR workflow for each. Dormant unless the operator sets both an MCP search tool and a positive poll interval on
/// the Work Tracking System connector, so an install that only uses the webhook pays nothing.
/// Every sweep re-resolves configuration, so enabling, retuning, or disabling the poll needs no restart.
/// </summary>
public sealed class JiraMcpTriggerPoller : BackgroundService
{
    /// <summary>How long a disabled poller waits before re-checking whether the operator switched it on.</summary>
    private static readonly TimeSpan DisabledRecheckInterval = TimeSpan.FromMinutes(1);

    /// <summary>Cap on issues returned per sweep — a safety valve against replaying a large backlog.</summary>
    private const int MaxIssuesPerSweep = 25;

    /// <summary>Window the first sweep looks back over, so a cold start picks up recent tickets but not history.</summary>
    private const int ColdStartLookbackMinutes = 15;

    private readonly IServiceProvider _services;
    private readonly ILogger<JiraMcpTriggerPoller> _logger;

    // Exclusive high-water mark: only tickets created strictly after this are started. Held in memory because a
    // replayed ticket is harmless — the orchestrator refuses to start a second run for a key it already has.
    private DateTimeOffset? _lastSeenCreatedAt;

    public JiraMcpTriggerPoller(IServiceProvider services, ILogger<JiraMcpTriggerPoller> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = DisabledRecheckInterval;
            try
            {
                interval = await RunSweepAsync(stoppingToken) ?? DisabledRecheckInterval;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A poll failure must never take the host down; the next sweep retries.
                _logger.LogWarning(ex, "The Jira MCP trigger poll failed; retrying on the next interval.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs one sweep and returns the interval until the next one, or null when the poll is switched off.
    /// Resolves everything from the connector store on each call so configuration changes apply immediately.
    /// </summary>
    private async Task<TimeSpan?> RunSweepAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var provider = scope.ServiceProvider;

        var pollInterval = await ResolvePollIntervalAsync(provider, ct);
        if (pollInterval is null)
            return null;

        var dorConfig = await provider.GetRequiredService<IDorConfigResolver>().ResolveActiveAsync(ct);
        if (!dorConfig.IsConfigured)
            return pollInterval;   // configured to poll, but the workflow itself is not ready yet

        var jql = JiraMcpClient.BuildCreatedSinceJql(
            dorConfig.Jira.ProjectKeys, dorConfig.Jira.IssueTypes, _lastSeenCreatedAt, ColdStartLookbackMinutes);

        var issues = await provider.GetRequiredService<IJiraMcpClient>()
            .SearchAsync(jql, MaxIssuesPerSweep, ct);
        if (issues.Count == 0)
            return pollInterval;

        await StartRunsAsync(provider.GetRequiredService<DorWorkflowOrchestrator>(), issues, ct);
        return pollInterval;
    }

    /// <summary>Starts a DoR run per issue, oldest first, advancing the high-water mark as each one is accepted.</summary>
    private async Task StartRunsAsync(
        DorWorkflowOrchestrator orchestrator, IReadOnlyList<JiraIssueSummary> issues, CancellationToken ct)
    {
        foreach (var issue in issues.OrderBy(candidate => candidate.CreatedAt ?? DateTimeOffset.MinValue))
        {
            var run = await orchestrator.StartAsync(issue.IssueKey, ct);
            if (run is not null)
                _logger.LogInformation("Started a DoR run for {IssueKey} from the Jira MCP trigger poll.", issue.IssueKey);

            // Advance regardless of whether a run started: a ticket the orchestrator declined (duplicate or out of
            // scope) must not be re-offered every sweep.
            if (issue.CreatedAt is { } createdAt && (_lastSeenCreatedAt is null || createdAt > _lastSeenCreatedAt))
                _lastSeenCreatedAt = createdAt;
        }
    }

    /// <summary>
    /// Returns the configured poll interval, or null when the poll is off — Jira is not the active provider, no
    /// MCP search tool is named, or the interval is not positive.
    /// </summary>
    private static async Task<TimeSpan?> ResolvePollIntervalAsync(IServiceProvider provider, CancellationToken ct)
    {
        var resolved = await provider.GetRequiredService<IWorkTrackerConfigResolver>().ResolveActiveAsync(ct);
        if (!resolved.IsConfigured || resolved.Provider != WorkTrackerProvider.Jira
            || string.IsNullOrWhiteSpace(resolved.NonSecretJson))
        {
            return null;
        }

        var config = System.Text.Json.JsonSerializer.Deserialize<JiraConnectorConfig>(
            resolved.NonSecretJson!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        if (config is null || string.IsNullOrWhiteSpace(config.McpServerUrl)
            || string.IsNullOrWhiteSpace(config.McpSearchToolName) || config.TriggerPollSeconds <= 0)
        {
            return null;
        }

        return TimeSpan.FromSeconds(config.TriggerPollSeconds);
    }
}
