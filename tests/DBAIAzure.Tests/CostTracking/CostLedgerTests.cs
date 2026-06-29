// Integration tests for SqlCostLedger using in-memory SQLite — cumulative, dimension-split totals.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.CostTracking;

public sealed class CostLedgerTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqlCostLedger _ledger;

    public CostLedgerTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();
        var options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(_keepAlive).Options;
        using (var seed = new PipelineDbContext(options)) seed.Database.EnsureCreated();
        _ledger = new SqlCostLedger(new SharedFactory(options), NullLogger<SqlCostLedger>.Instance);
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task GetTotals_SumsByDimension_CumulativeAcrossAppends_NoOverwrite()
    {
        await _ledger.AppendAsync(Entry("BIND-X", CostDimension.Runtime, 1.50));
        await _ledger.AppendAsync(Entry("BIND-X", CostDimension.Runtime, 0.50));   // accumulates, not overwrites
        await _ledger.AppendAsync(Entry("BIND-X", CostDimension.Development, 4.00));
        await _ledger.AppendAsync(Entry("BIND-OTHER", CostDimension.Runtime, 99.0)); // different key — excluded

        var totals = await _ledger.GetTotalsAsync("BIND-X");

        Assert.Equal(2.00, totals.RuntimeUsd);       // 1.50 + 0.50 (cumulative)
        Assert.Equal(4.00, totals.DevelopmentUsd);
    }

    [Fact]
    public async Task GetTotals_UnknownKey_ReturnsZero()
    {
        var totals = await _ledger.GetTotalsAsync("BIND-NONE");
        Assert.Equal(0, totals.RuntimeUsd);
        Assert.Equal(0, totals.DevelopmentUsd);
    }

    private static CostLedgerEntry Entry(string key, CostDimension dimension, double cost) => new()
    {
        Id = Guid.NewGuid(),
        BindingKey = key,
        Dimension = dimension,
        CostUsd = cost,
        OccurredAt = DateTimeOffset.UtcNow,
    };

    private sealed class SharedFactory(DbContextOptions<PipelineDbContext> options)
        : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }
}
