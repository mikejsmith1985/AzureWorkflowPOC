// Unit test for CostProjectionService — writes both cumulative cost fields from the ledger totals.
using DBAIAzure.Core.Interfaces;
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
        var boards = new FakeBoardsClient();
        var service = new CostProjectionService(
            new StubLedger(new CostTotals(RuntimeUsd: 2.50, DevelopmentUsd: 4.00)),
            boards, NullLogger<CostProjectionService>.Instance);

        await service.ProjectAsync("BIND-X", workItemId: 42);

        var fields = Assert.Single(boards.FieldUpdates).Fields;
        Assert.Equal(2.50, fields["Custom.AIRuntimeCostUSD"]);
        Assert.Equal(4.00, fields["Custom.AIDevCostUSD"]);
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
