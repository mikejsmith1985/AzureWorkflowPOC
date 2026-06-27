// IConnectorAdapter implementation for the Messaging connector — sends notify-node messages via the delivery seam.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DBAIAzure.Connectors;

/// <summary>
/// Drives the Messaging connector from a workflow notify node. Parses the node's realized payload JSON
/// for the message text and delegates delivery to <see cref="IMessageDelivery"/>, which chooses the MCP
/// or webhook path and the platform. Delivery failures are surfaced in the returned outcome string rather
/// than thrown, so a notify node never aborts the run (FR-010).
/// </summary>
public sealed class MessagingConnectorAdapter : IConnectorAdapter
{
    private readonly IMessageDelivery _delivery;
    private readonly ILogger<MessagingConnectorAdapter> _logger;

    /// <inheritdoc/>
    public ConnectorType ConnectorType => ConnectorType.Messaging;

    public MessagingConnectorAdapter(IMessageDelivery delivery, ILogger<MessagingConnectorAdapter> logger)
    {
        _delivery = delivery;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> ExecuteAsync(string payload, CancellationToken ct = default)
    {
        var message = "<no message>";
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("message", out var messageEl))
                message = messageEl.GetString() ?? message;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "MessagingConnectorAdapter: payload is not valid JSON — {Payload}", payload);
        }

        var result = await _delivery.SendAsync(message, ct);
        if (!result.IsSuccess)
            _logger.LogWarning("MessagingConnectorAdapter: delivery failed — {Detail}", result.Message);

        return result.Message;
    }

    /// <inheritdoc/>
    public Task<ConnectorTestResult> HealthCheckAsync(CancellationToken ct = default) =>
        _delivery.TestConnectionAsync(ct);
}
