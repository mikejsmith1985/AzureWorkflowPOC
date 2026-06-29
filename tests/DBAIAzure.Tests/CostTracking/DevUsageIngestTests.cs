// Tests for TelemetryIngestController dev-usage ingest — attributed/unattributed entries + secret gate.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Web.Controllers;
using DBAIAzure.Web.Integrations.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.CostTracking;

public sealed class DevUsageIngestTests
{
    private const string Secret = "sekret";
    private const string SecretHeader = "X-Telemetry-Secret";

    [Fact]
    public async Task ValidKey_AppendsAttributedDevelopmentEntry_CostRepriced()
    {
        var ledger = new CapturingLedger();
        var controller = BuildController(ledger, new StubMap(workItemId: 100), withSecret: true);

        var result = await controller.ReceiveDevUsage(new DevUsageIngestPayload
        {
            BindingKey = "BIND-X",
            Model = "claude-sonnet-4-6",
            InputTokens = 1000,
            OutputTokens = 500,
            SessionId = "sess-1",
            // cost_usd omitted → re-priced from tokens
        });

        Assert.IsType<AcceptedResult>(result);
        var entry = Assert.Single(ledger.Entries);
        Assert.Equal(CostDimension.Development, entry.Dimension);
        Assert.Equal("100", entry.WorkItemId);
        Assert.False(entry.IsUnattributed);
        Assert.True(entry.CostUsd > 0, "cost should be re-priced from tokens");
    }

    [Fact]
    public async Task UnknownKey_RecordsUnattributedEntry()
    {
        var ledger = new CapturingLedger();
        var controller = BuildController(ledger, new StubMap(workItemId: null), withSecret: true);

        var result = await controller.ReceiveDevUsage(new DevUsageIngestPayload
        {
            BindingKey = "BIND-NONE", Model = "claude-sonnet-4-6", InputTokens = 10, OutputTokens = 5,
        });

        Assert.IsType<AcceptedResult>(result);
        var entry = Assert.Single(ledger.Entries);
        Assert.True(entry.IsUnattributed);
        Assert.Null(entry.WorkItemId);
    }

    [Fact]
    public async Task MissingSecret_ReturnsUnauthorized_NothingAppended()
    {
        var ledger = new CapturingLedger();
        var controller = BuildController(ledger, new StubMap(workItemId: 100), withSecret: false);

        var result = await controller.ReceiveDevUsage(new DevUsageIngestPayload { BindingKey = "BIND-X" });

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Empty(ledger.Entries);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static TelemetryIngestController BuildController(ICostLedger ledger, IBindingWorkItemMap map, bool withSecret)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["WebhookSecrets:Telemetry"] = Secret })
            .Build();

        var controller = new TelemetryIngestController(
            ledger, map, new NoOpProjection(), config, NullLogger<TelemetryIngestController>.Instance);

        var httpContext = new DefaultHttpContext();
        if (withSecret)
            httpContext.Request.Headers[SecretHeader] = Secret;
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private sealed class CapturingLedger : ICostLedger
    {
        public List<CostLedgerEntry> Entries { get; } = [];
        public Task AppendAsync(CostLedgerEntry entry, CancellationToken ct = default) { Entries.Add(entry); return Task.CompletedTask; }
        public Task<CostTotals> GetTotalsAsync(string bindingKey, CancellationToken ct = default) => Task.FromResult(new CostTotals(0, 0));
    }

    private sealed class StubMap : IBindingWorkItemMap
    {
        private readonly int? _workItemId;
        public StubMap(int? workItemId) => _workItemId = workItemId;
        public Task PutAsync(string bindingKey, WorkItemRef workItem, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WorkItemRef?> ResolveAsync(string bindingKey, CancellationToken ct = default) =>
            Task.FromResult(_workItemId is int id ? (WorkItemRef?)WorkItemRef.From(id) : null);
    }

    private sealed class NoOpProjection : ICostProjection
    {
        public Task ProjectAsync(string bindingKey, WorkItemRef workItem, CancellationToken ct = default) => Task.CompletedTask;
    }
}
