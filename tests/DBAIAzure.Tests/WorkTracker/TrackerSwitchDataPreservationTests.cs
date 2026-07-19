// FR-011 (spec-018 T037): the cost ledger + binding map are tracker-neutral — switching the active
// work tracker leaves existing rows intact and still resolvable. Selection is orthogonal to the data.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Storage;
using DBAIAzure.Storage.Repositories;
using DBAIAzure.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests.WorkTracker;

public sealed class TrackerSwitchDataPreservationTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqlBindingWorkItemMap _bindingMap;
    private readonly SqlCostLedger _ledger;

    public TrackerSwitchDataPreservationTests()
    {
        _keepAlive = new SqliteConnection("Data Source=:memory:");
        _keepAlive.Open();
        var options = new DbContextOptionsBuilder<PipelineDbContext>().UseSqlite(_keepAlive).Options;
        using (var seed = new PipelineDbContext(options)) seed.Database.EnsureCreated();
        var factory = new SharedFactory(options);
        _bindingMap = new SqlBindingWorkItemMap(factory);
        _ledger = new SqlCostLedger(factory, NullLogger<SqlCostLedger>.Instance);
    }

    public void Dispose() => _keepAlive.Dispose();

    [Fact]
    public async Task LedgerAndBinding_SurviveTrackerSwitch()
    {
        // Data written while one tracker is active.
        await _bindingMap.PutAsync("BIND-X", new WorkItemRef("PROJ-1"));
        await _ledger.AppendAsync(new CostLedgerEntry
        {
            Id = Guid.NewGuid(), BindingKey = "BIND-X", Dimension = CostDimension.Runtime,
            CostUsd = 2.50, OccurredAt = DateTimeOffset.UtcNow,
        });

        var adapters = new IWorkTrackerAdapter[] { new KeyedStubAdapter("AzureDevOps"), new KeyedStubAdapter("Jira") };

        // Switch the active tracker by reconfiguration — selection is the only thing that changes.
        Assert.Equal("AzureDevOps", new WorkTrackerAdapterProvider(adapters, ResolverFor(Core.Models.WorkTrackerProvider.AzureDevOps)).GetAdapter().TrackerKey);
        Assert.Equal("Jira", new WorkTrackerAdapterProvider(adapters, ResolverFor(Core.Models.WorkTrackerProvider.Jira)).GetAdapter().TrackerKey);

        // The tracker-neutral cost ledger + binding rows are intact and still resolvable after the switch.
        Assert.Equal(new WorkItemRef("PROJ-1"), await _bindingMap.ResolveAsync("BIND-X"));
        Assert.Equal(2.50, (await _ledger.GetTotalsAsync("BIND-X")).RuntimeUsd);
    }

    private static IWorkTrackerConfigResolver ResolverFor(Core.Models.WorkTrackerProvider provider) =>
        new StubResolver(provider);

    private sealed class StubResolver(Core.Models.WorkTrackerProvider provider) : IWorkTrackerConfigResolver
    {
        public Task<Core.Models.ResolvedWorkTrackerConfig> ResolveActiveAsync(CancellationToken ct = default) =>
            Task.FromResult(new Core.Models.ResolvedWorkTrackerConfig(provider, "{}", null, IsConfigured: true));
    }

    private sealed class SharedFactory(DbContextOptions<PipelineDbContext> options)
        : IDbContextFactory<PipelineDbContext>
    {
        public PipelineDbContext CreateDbContext() => new(options);
    }

    // Minimal adapter whose only relevant trait is its TrackerKey (no method is invoked by this test).
    private sealed class KeyedStubAdapter : IWorkTrackerAdapter
    {
        public KeyedStubAdapter(string trackerKey) => TrackerKey = trackerKey;
        public string TrackerKey { get; }
        public Task<Core.Models.CreatedWorkItemRef> CreateWorkItemAsync(WorkItemType type, string title, string description, WorkItemRef? parent, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Core.Models.CreatedWorkItemRef> UpsertWorkItemAsync(WorkItemRef item, string title, string description, string appendComment, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AppendCommentAsync(WorkItemRef item, string comment, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetFieldsAsync(WorkItemRef item, IReadOnlyDictionary<string, object?> logicalFields, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WorkItemRef?> ResolveByBindingKeyAsync(string bindingKey, CancellationToken ct = default) => Task.FromResult<WorkItemRef?>(null);
        public Task<ProvisioningResult> ProvisionFieldsAsync(AdoTelemetryFieldConfig fieldConfig, CancellationToken ct = default) => Task.FromResult(new ProvisioningResult { IsSuccess = true, Mode = "Stub" });
        public RollupCapability GetRollupCapability() => new(RollupKind.None);
    }
}
