// Functional connectivity test for the Messaging connector — delegates to the shared delivery seam (FR-008).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors;

/// <summary>
/// Tests the Messaging connector by sending a labelled connectivity-test message through
/// <see cref="IMessageDelivery"/>, which selects the MCP or webhook path and reports the platform and
/// path used (FR-008, FR-009). Kept as a thin type so <see cref="ConnectorHealthChecker"/> can resolve
/// it per <see cref="ConnectorType"/> alongside the other connector testers.
/// </summary>
public sealed class MessagingConnectorTester
{
    private readonly IMessageDelivery _delivery;

    public MessagingConnectorTester(IMessageDelivery delivery) => _delivery = delivery;

    /// <summary>Sends a labelled test message via the configured delivery path and returns the outcome.</summary>
    public Task<ConnectorTestResult> TestConnectionAsync(CancellationToken ct = default) =>
        _delivery.TestConnectionAsync(ct);
}
