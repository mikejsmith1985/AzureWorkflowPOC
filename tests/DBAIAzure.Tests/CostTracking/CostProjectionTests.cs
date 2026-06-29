// Unit test for CostProjectionService — writes both cumulative cost fields (logical) via the active adapter.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Tests.Fakes;
using DBAIAzure.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.CostTracking;

public sealed class CostProjectionTests
{
    [Fact]
    public async Task ProjectAsync_WritesBothCostFields_FromLedgerTotals()
    {
        var tracker = new FakeWorkTrackerAdapter();
        var service = new CostProjectionService(
            new StubLedger(new CostTotals(RuntimeUsd: 2.50, DevelopmentUsd: 4.00)),
            new SingleAdapterProvider(tracker), NullLogger<CostProjectionService>.Instance);

        await service.ProjectAsync("BIND-X", WorkItemRef.From(42));

        var (item, fields) = Assert.Single(tracker.FieldSets);
        Assert.Equal(WorkItemRef.From(42), item);
        Assert.Equal(2.50, fields[LogicalField.AIRuntimeCostUSD]);
        Assert.Equal(4.00, fields[LogicalField.AIDevCostUSD]);
    }

    private sealed class StubLedger : ICostLedger
    {
        private readonly CostTotals _totals;
        public StubLedger(CostTotals totals) => _totals = totals;

        public Task AppendAsync(Core.Models.AdoTelemetry.CostLedgerEntry entry, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<CostTotals> GetTotalsAsync(string bindingKey, CancellationToken ct = default)
            => Task.FromResult(_totals);
    }
}
