// Functional connectivity testing for all four pipeline connectors (FR-008 through FR-011, FR-018).
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Executes functional connectivity tests against the four configured connectors. Each test
/// authenticates with stored credentials and exercises the target resource — not just a ping.
/// Used from the Blazor modal (per-connector) and by orchestrators (pre-flight gate, FR-018).
/// </summary>
public interface IConnectorHealthChecker
{
    /// <summary>
    /// Runs a functional test against the specified connector using currently stored credentials.
    /// Always performs a live round-trip — never reads the cached <c>LastTestResult</c> from storage.
    /// After the test, persists the result via <see cref="IConnectorConfigRepository.UpdateTestResultAsync"/>.
    /// </summary>
    Task<ConnectorTestResult> TestAsync(ConnectorType type, CancellationToken ct = default);

    /// <summary>
    /// Runs functional tests against all four connectors in parallel (<c>Task.WhenAll</c>).
    /// Returns one <see cref="ConnectorTestResult"/> per connector. Used as the pre-flight gate
    /// before every pipeline run (FR-018, SC-008). Total duration is bounded by the slowest test.
    /// </summary>
    Task<IReadOnlyList<ConnectorTestResult>> CheckAllAsync(CancellationToken ct = default);
}
