// Tests for ConnectorHealthChecker: all-pass, single-fail, pre-flight diagnostic (T019, T031).
using DBAIAzure.Connectors;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests;

/// <summary>
/// Unit tests for ConnectorHealthChecker using the internal constructor that accepts per-connector
/// delegate fakes — no real HTTP connections or connectors are exercised.
/// </summary>
public sealed class ConnectorHealthCheckerTests
{
    // ── Helpers ─────────────────────────────────────────────────────────

    private static ConnectorHealthChecker BuildChecker(
        IReadOnlyDictionary<ConnectorType, Func<CancellationToken, Task<ConnectorTestResult>>> testers,
        IConnectorConfigRepository? repo = null)
    {
        repo ??= new NullConfigRepository();
        return new ConnectorHealthChecker(testers, repo, NullLogger<ConnectorHealthChecker>.Instance);
    }

    private static Func<CancellationToken, Task<ConnectorTestResult>> PassTester(ConnectorType type) =>
        _ => Task.FromResult(new ConnectorTestResult(type, true, "Connected.", DateTimeOffset.UtcNow));

    private static Func<CancellationToken, Task<ConnectorTestResult>> FailTester(ConnectorType type, string reason) =>
        _ => Task.FromResult(new ConnectorTestResult(type, false, reason, DateTimeOffset.UtcNow));

    private static IReadOnlyDictionary<ConnectorType, Func<CancellationToken, Task<ConnectorTestResult>>> AllPassTesters() =>
        new Dictionary<ConnectorType, Func<CancellationToken, Task<ConnectorTestResult>>>
        {
            [ConnectorType.ServiceNow]  = PassTester(ConnectorType.ServiceNow),
            [ConnectorType.AzureDevOps] = PassTester(ConnectorType.AzureDevOps),
            [ConnectorType.LLM]         = PassTester(ConnectorType.LLM),
            [ConnectorType.Teams]       = PassTester(ConnectorType.Teams),
        };

    // ── All-pass ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAllAsync_WhenAllPass_ReturnsAllSuccessTrue()
    {
        var checker = BuildChecker(AllPassTesters());

        var results = await checker.CheckAllAsync();

        Assert.Equal(4, results.Count);
        Assert.All(results, r => Assert.True(r.IsSuccess));
    }

    // ── Single failure ───────────────────────────────────────────────────

    [Fact]
    public async Task CheckAllAsync_WhenOneConnectorFails_ReturnsItAsIsSuccessFalse()
    {
        var testers = new Dictionary<ConnectorType, Func<CancellationToken, Task<ConnectorTestResult>>>
        {
            [ConnectorType.ServiceNow]  = PassTester(ConnectorType.ServiceNow),
            [ConnectorType.AzureDevOps] = FailTester(ConnectorType.AzureDevOps, "PAT expired"),
            [ConnectorType.LLM]         = PassTester(ConnectorType.LLM),
            [ConnectorType.Teams]       = PassTester(ConnectorType.Teams),
        };

        var checker = BuildChecker(testers);
        var results = await checker.CheckAllAsync();

        var adoResult = results.Single(r => r.Type == ConnectorType.AzureDevOps);
        Assert.False(adoResult.IsSuccess);
        Assert.Equal("PAT expired", adoResult.Message);

        // Other three still pass.
        Assert.Equal(3, results.Count(r => r.IsSuccess));
    }

    // ── TestAsync for individual type ────────────────────────────────────

    [Fact]
    public async Task TestAsync_ForPassingType_ReturnsSuccessResult()
    {
        var checker = BuildChecker(AllPassTesters());

        var result = await checker.TestAsync(ConnectorType.LLM);

        Assert.True(result.IsSuccess);
        Assert.Equal(ConnectorType.LLM, result.Type);
    }

    [Fact]
    public async Task TestAsync_ForFailingType_ReturnsFailResult()
    {
        const string failReason = "401 Unauthorized";
        var testers = new Dictionary<ConnectorType, Func<CancellationToken, Task<ConnectorTestResult>>>
        {
            [ConnectorType.LLM] = FailTester(ConnectorType.LLM, failReason),
        };

        var checker = BuildChecker(testers);
        var result  = await checker.TestAsync(ConnectorType.LLM);

        Assert.False(result.IsSuccess);
        Assert.Equal(failReason, result.Message);
    }

    // ── Pre-flight diagnostic (T031): ADO PAT revoked — other three pass ─

    [Fact]
    public async Task CheckAllAsync_WithRevokedAdoPat_ReturnsSpecificDiagnostic()
    {
        const string revokedMessage = "ADO PAT has been revoked — please rotate it in the connector modal.";
        var testers = new Dictionary<ConnectorType, Func<CancellationToken, Task<ConnectorTestResult>>>
        {
            [ConnectorType.ServiceNow]  = PassTester(ConnectorType.ServiceNow),
            [ConnectorType.AzureDevOps] = FailTester(ConnectorType.AzureDevOps, revokedMessage),
            [ConnectorType.LLM]         = PassTester(ConnectorType.LLM),
            [ConnectorType.Teams]       = PassTester(ConnectorType.Teams),
        };

        var checker  = BuildChecker(testers);
        var results  = await checker.CheckAllAsync();
        var failures = results.Where(r => !r.IsSuccess).ToList();

        Assert.Single(failures);
        Assert.Equal(ConnectorType.AzureDevOps, failures[0].Type);
        Assert.Contains("revoked", failures[0].Message, StringComparison.OrdinalIgnoreCase);

        // Verify that a PipelinePreflightFailure built from this result carries the same diagnostic.
        var preflightFailure = new PipelinePreflightFailure(failures.AsReadOnly());
        Assert.Single(preflightFailure.FailingConnectors);
    }

    // ── Unhandled exception from a tester ────────────────────────────────

    [Fact]
    public async Task TestAsync_WhenTesterThrows_ReturnsSafeFailResult()
    {
        var testers = new Dictionary<ConnectorType, Func<CancellationToken, Task<ConnectorTestResult>>>
        {
            [ConnectorType.Teams] = _ => throw new InvalidOperationException("Network error"),
        };

        var checker = BuildChecker(testers);
        var result  = await checker.TestAsync(ConnectorType.Teams);

        Assert.False(result.IsSuccess);
        Assert.Equal(ConnectorType.Teams, result.Type);
    }

    // ── Null config repository (DI safety — health checker must not crash if repo is absent) ──

    [Fact]
    public async Task CheckAllAsync_WhenPersistenceThrows_StillReturnsResults()
    {
        var checker = BuildChecker(AllPassTesters(), repo: new AlwaysThrowingConfigRepository());

        // Persistence failure should not propagate — results still returned.
        var results = await checker.CheckAllAsync();
        Assert.Equal(4, results.Count);
    }

    // ── Fakes ────────────────────────────────────────────────────────────

    private sealed class NullConfigRepository : IConnectorConfigRepository
    {
        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) => Task.FromResult<ConnectorConfig?>(null);
        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConnectorConfig>>(Array.Empty<ConnectorConfig>());
        public Task SaveAsync(ConnectorType type, string? nonSecretJson, string? plaintextSecretsJson, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AlwaysThrowingConfigRepository : IConnectorConfigRepository
    {
        public Task<ConnectorConfig?> GetAsync(ConnectorType type, CancellationToken ct = default) => Task.FromResult<ConnectorConfig?>(null);
        public Task<IReadOnlyList<ConnectorConfig>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ConnectorConfig>>(Array.Empty<ConnectorConfig>());
        public Task SaveAsync(ConnectorType type, string? nonSecretJson, string? plaintextSecretsJson, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string?> GetDecryptedSecretsAsync(ConnectorType type, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task UpdateTestResultAsync(ConnectorType type, ConnectorTestResult result, CancellationToken ct = default) =>
            Task.FromException(new InvalidOperationException("Simulated persistence failure"));
    }
}
