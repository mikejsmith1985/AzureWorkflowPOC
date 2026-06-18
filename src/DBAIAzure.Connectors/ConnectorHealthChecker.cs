// Orchestrates functional tests across all four pipeline connectors (IConnectorHealthChecker).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.Logging;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("DBAIAzure.Tests")]

namespace DBAIAzure.Connectors;

/// <summary>
/// Runs functional tests across all four connectors and persists results via
/// <see cref="IConnectorConfigRepository.UpdateTestResultAsync"/>. Used both from the Blazor
/// modal (single connector) and from pipeline orchestrators (pre-flight gate, FR-018, SC-008).
/// </summary>
public sealed class ConnectorHealthChecker : IConnectorHealthChecker
{
    // Per-connector test functions stored at construction, allowing unit tests to inject fakes
    // without needing to stub full connector clients (see internal constructor below).
    private readonly IReadOnlyDictionary<ConnectorType, Func<CancellationToken, Task<ConnectorTestResult>>> _testers;
    private readonly IConnectorConfigRepository _configRepo;
    private readonly ILogger<ConnectorHealthChecker> _logger;

    /// <summary>Production constructor — wires the four concrete connector testers.</summary>
    public ConnectorHealthChecker(
        ServiceNowClient snowClient,
        AdoConnectorTester adoTester,
        LlmConnectorTester llmTester,
        TeamsConnectorTester teamsTester,
        IConnectorConfigRepository configRepo,
        ILogger<ConnectorHealthChecker> logger)
    {
        _configRepo = configRepo;
        _logger = logger;
        _testers = new Dictionary<ConnectorType, Func<CancellationToken, Task<ConnectorTestResult>>>
        {
            [ConnectorType.ServiceNow] = snowClient.TestConnectionAsync,
            [ConnectorType.AzureDevOps] = adoTester.TestConnectionAsync,
            [ConnectorType.LLM]         = llmTester.TestConnectionAsync,
            [ConnectorType.Teams]       = teamsTester.TestConnectionAsync,
        };
    }

    /// <summary>
    /// Internal constructor for unit tests: accepts per-connector test delegates directly so tests
    /// can return preset results without standing up real HTTP connections.
    /// </summary>
    internal ConnectorHealthChecker(
        IReadOnlyDictionary<ConnectorType, Func<CancellationToken, Task<ConnectorTestResult>>> testers,
        IConnectorConfigRepository configRepo,
        ILogger<ConnectorHealthChecker> logger)
    {
        _testers = testers;
        _configRepo = configRepo;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ConnectorTestResult> TestAsync(ConnectorType type, CancellationToken ct = default)
    {
        if (!_testers.TryGetValue(type, out var tester))
            throw new ArgumentOutOfRangeException(nameof(type), type, "No tester registered for this connector type.");

        ConnectorTestResult result;
        try
        {
            result = await tester(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connector test for {ConnectorType} threw an unhandled exception", type);
            result = new ConnectorTestResult(
                type, false,
                "Stored credentials could not be decrypted — please re-enter them.",
                DateTimeOffset.UtcNow);
        }

        // Persist the result so the modal can display it without re-running the test.
        try
        {
            await _configRepo.UpdateTestResultAsync(type, result, ct);
        }
        catch (Exception ex)
        {
            // Persistence failure does not fail the test itself.
            _logger.LogWarning(ex, "Failed to persist test result for {ConnectorType}", type);
        }

        return result;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConnectorTestResult>> CheckAllAsync(CancellationToken ct = default)
    {
        // All four tests run concurrently — total wall-clock is bounded by the slowest test.
        var tasks = _testers.Keys.Select(type => TestAsync(type, ct)).ToArray();
        var results = await Task.WhenAll(tasks);
        return results.ToList().AsReadOnly();
    }
}
