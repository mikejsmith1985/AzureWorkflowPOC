// Integration tests for SqlBindingWorkItemMap using in-memory SQLite — put/resolve round-trip (C1).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DBAIAzure.Tests.CostTracking;

public sealed class BindingWorkItemMapTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqlBindingWorkItemMap _map;

    public BindingWorkItemMapTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();
        var options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(_keepAlive).Options;
        using (var seed = new PipelineDbContext(options)) seed.Database.EnsureCreated();
        _map = new SqlBindingWorkItemMap(new SharedFactory(options));
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task Put_Then_Resolve_RoundTrips()
    {
        await _map.PutAsync("BIND-ABC", WorkItemRef.From(4242));
        Assert.Equal(WorkItemRef.From(4242), await _map.ResolveAsync("BIND-ABC"));
    }

    [Fact]
    public async Task Resolve_UnknownKey_ReturnsNull()
    {
        Assert.Null(await _map.ResolveAsync("BIND-NONE"));
    }

    [Fact]
    public async Task Put_SameKeyTwice_UpdatesWorkItem()
    {
        await _map.PutAsync("BIND-DUP", WorkItemRef.From(1));
        await _map.PutAsync("BIND-DUP", WorkItemRef.From(2));
        Assert.Equal(WorkItemRef.From(2), await _map.ResolveAsync("BIND-DUP"));
    }

    private sealed class SharedFactory(DbContextOptions<PipelineDbContext> options)
        : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }
}
