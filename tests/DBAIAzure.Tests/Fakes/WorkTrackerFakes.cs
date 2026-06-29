// Test doubles for the work-tracker adapter seam (spec-018).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;
using DBAIAzure.Web.Integrations.AzureDevOps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DBAIAzure.Tests.Fakes;

/// <summary>Builds a real ADO adapter wrapping a (fake) boards client — so phase-handler tests can register
/// <see cref="IWorkTrackerAdapter"/> in the kernel while still asserting on the underlying boards calls.</summary>
public static class WorkTrackerAdapters
{
    public static IWorkTrackerAdapter AdoAdapterFor(IBoardsClient boards, IBindingWorkItemMap? bindingMap = null)
    {
        var scopeFactory = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        return new AzureDevOpsWorkTrackerAdapter(
            boards, bindingMap ?? new NullBindingMap(), scopeFactory,
            new AdoFieldReferenceResolver(), NullLogger<AzureDevOpsWorkTrackerAdapter>.Instance);
    }

    private sealed class NullBindingMap : IBindingWorkItemMap
    {
        public Task PutAsync(string bindingKey, WorkItemRef workItem, CancellationToken ct = default) => Task.CompletedTask;
        public Task<WorkItemRef?> ResolveAsync(string bindingKey, CancellationToken ct = default) => Task.FromResult<WorkItemRef?>(null);
    }
}

/// <summary>Records adapter calls so tests can assert tracker-neutral behaviour without a real tracker.</summary>
public sealed class FakeWorkTrackerAdapter : IWorkTrackerAdapter
{
    public string TrackerKey => "Fake";

    public List<(WorkItemType Type, string Title, WorkItemRef? Parent)> Creates { get; } = [];
    public List<(WorkItemRef Item, IReadOnlyDictionary<string, object?> Fields)> FieldSets { get; } = [];
    public WorkItemRef? ResolveResult { get; set; }

    private int _nextId = 5000;

    public Task<CreatedWorkItemRef> CreateWorkItemAsync(
        WorkItemType type, string title, string description, WorkItemRef? parent, CancellationToken ct = default)
    {
        Creates.Add((type, title, parent));
        return Task.FromResult(new CreatedWorkItemRef
        {
            WorkItemId = WorkItemRef.From(_nextId++),
            WorkItemType = type.ToString(),
            Url = string.Empty,
            WasUpdated = false,
        });
    }

    public Task<CreatedWorkItemRef> UpsertWorkItemAsync(WorkItemRef item, string title, string description, string appendComment, CancellationToken ct = default)
        => Task.FromResult(new CreatedWorkItemRef { WorkItemId = item, WorkItemType = string.Empty, Url = string.Empty, WasUpdated = true });

    public Task AppendCommentAsync(WorkItemRef item, string comment, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task SetFieldsAsync(WorkItemRef item, IReadOnlyDictionary<string, object?> logicalFields, CancellationToken ct = default)
    {
        FieldSets.Add((item, logicalFields));
        return Task.CompletedTask;
    }

    public Task<WorkItemRef?> ResolveByBindingKeyAsync(string bindingKey, CancellationToken ct = default)
        => Task.FromResult(ResolveResult);

    public Task<ProvisioningResult> ProvisionFieldsAsync(AdoTelemetryFieldConfig fieldConfig, CancellationToken ct = default)
        => Task.FromResult(new ProvisioningResult { IsSuccess = true, Mode = "Fake" });

    public RollupCapability GetRollupCapability() => new(RollupKind.Native, "Fake");
}

/// <summary>A provider that always returns the one adapter it was given.</summary>
public sealed class SingleAdapterProvider : IWorkTrackerAdapterProvider
{
    private readonly IWorkTrackerAdapter _adapter;
    public SingleAdapterProvider(IWorkTrackerAdapter adapter) => _adapter = adapter;
    public IWorkTrackerAdapter GetAdapter(WorkRoutingContext? routingContext = null) => _adapter;
}
