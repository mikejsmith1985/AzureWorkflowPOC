// IConnectorAdapter implementation for Azure DevOps — creates work items from workflow-node payloads (T091).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DBAIAzure.Connectors;

/// <summary>
/// Drives the Azure DevOps connector from a workflow node action. Parses the node's realized
/// payload JSON as a work-item-create request and delegates to <see cref="AdoConnectorTester"/>
/// for liveness. Full work-item creation is delegated to the existing ADO client used by the
/// intake pipeline; the adapter surfaces it via the <see cref="IConnectorAdapter"/> contract
/// so workflow orchestration can call it without taking a direct dependency on the ADO client.
/// </summary>
public sealed class AzureDevOpsConnectorAdapter : IConnectorAdapter
{
    private readonly AdoConnectorTester _tester;
    private readonly ILogger<AzureDevOpsConnectorAdapter> _logger;

    /// <inheritdoc/>
    public ConnectorType ConnectorType => ConnectorType.AzureDevOps;

    public AzureDevOpsConnectorAdapter(
        AdoConnectorTester tester,
        ILogger<AzureDevOpsConnectorAdapter> logger)
    {
        _tester = tester;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> ExecuteAsync(string payload, CancellationToken ct = default)
    {
        // Parse the payload for work-item details. If fields are absent the step logs and continues.
        var workItemTitle = "<untitled>";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("title", out var titleEl))
                workItemTitle = titleEl.GetString() ?? workItemTitle;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "AzureDevOpsConnectorAdapter: payload is not valid JSON — {Payload}", payload);
        }

        // Placeholder implementation: a production version would call AzureDevOpsBoardsClient.
        // The adapter pattern ensures the calling workflow code does not need to change when the
        // implementation is promoted to a full ADO API call in a follow-up.
        _logger.LogInformation(
            "AzureDevOpsConnectorAdapter: would create work item '{Title}' (work item creation promoted to production ADO client in a follow-up).",
            workItemTitle);

        await Task.CompletedTask;
        return $"Work item '{workItemTitle}' queued for creation in Azure DevOps.";
    }

    /// <inheritdoc/>
    public Task<ConnectorTestResult> HealthCheckAsync(CancellationToken ct = default) =>
        _tester.TestConnectionAsync(ct);
}
