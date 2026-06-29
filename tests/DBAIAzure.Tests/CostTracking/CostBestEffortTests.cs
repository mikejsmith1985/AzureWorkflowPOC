// Non-blocking test (spec-017 FR-011, U1): a throwing ledger must not bubble out of cost projection.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Tests.Fakes;
using DBAIAzure.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.CostTracking;

public sealed class CostBestEffortTests
{
    [Fact]
    public async Task Projection_WhenLedgerThrows_DoesNotBubbleOut()
    {
        // FR-011: cost capture is best-effort — a ledger failure must never disrupt the caller.
        var service = new CostProjectionService(
            new ThrowingLedger(), new SingleAdapterProvider(new FakeWorkTrackerAdapter()),
            NullLogger<CostProjectionService>.Instance);

        var exception = await Record.ExceptionAsync(() => service.ProjectAsync("BIND-X", WorkItemRef.From(1)));

        Assert.Null(exception);
    }

    private sealed class ThrowingLedger : ICostLedger
    {
        public Task AppendAsync(CostLedgerEntry entry, CancellationToken ct = default)
            => throw new InvalidOperationException("ledger down");
        public Task<CostTotals> GetTotalsAsync(string bindingKey, CancellationToken ct = default)
            => throw new InvalidOperationException("ledger down");
    }
}
