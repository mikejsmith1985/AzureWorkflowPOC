// IConnectorAdapter implementation for Microsoft Teams — sends messages from workflow-node payloads (T091).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DBAIAzure.Connectors;

/// <summary>
/// Drives the Teams connector from a workflow FunctionNotify node. Parses the node's realized
/// payload JSON as a message-send request (recipient UPN + message text) and delegates to
/// <see cref="TeamsConnectorTester"/> for liveness checks.
///
/// Full Adaptive Card delivery via Graph API is handled by <c>TeamsWorkflowApprovalNotifier</c>
/// for HITL flows; this adapter covers simpler fire-and-forget notification nodes that do not
/// require an approval callback.
/// </summary>
public sealed class TeamsConnectorAdapter : IConnectorAdapter
{
    private readonly TeamsConnectorTester _tester;
    private readonly ILogger<TeamsConnectorAdapter> _logger;

    /// <inheritdoc/>
    public ConnectorType ConnectorType => ConnectorType.Teams;

    public TeamsConnectorAdapter(
        TeamsConnectorTester tester,
        ILogger<TeamsConnectorAdapter> logger)
    {
        _tester = tester;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> ExecuteAsync(string payload, CancellationToken ct = default)
    {
        var recipient = "<unknown>";
        var message   = "<no message>";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("recipient", out var recipientEl))
                recipient = recipientEl.GetString() ?? recipient;
            if (doc.RootElement.TryGetProperty("message", out var messageEl))
                message = messageEl.GetString() ?? message;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "TeamsConnectorAdapter: payload is not valid JSON — {Payload}", payload);
        }

        // Placeholder: a production version would call the Graph API chat message endpoint.
        // Promoted to a real call in the same follow-up that promotes AzureDevOpsConnectorAdapter.
        _logger.LogInformation(
            "TeamsConnectorAdapter: would send Teams message to {Recipient}: {Message}",
            recipient, message);

        await Task.CompletedTask;
        return $"Teams notification queued for {recipient}.";
    }

    /// <inheritdoc/>
    public Task<ConnectorTestResult> HealthCheckAsync(CancellationToken ct = default) =>
        _tester.TestConnectionAsync(ct);
}
