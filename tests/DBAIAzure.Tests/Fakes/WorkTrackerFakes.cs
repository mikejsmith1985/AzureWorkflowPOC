// Test doubles for the work-tracker adapter seam (spec-018).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;

namespace DBAIAzure.Tests.Fakes;

/// <summary>Records adapter calls so tests can assert tracker-neutral behaviour without a real tracker.</summary>
public sealed class FakeWorkTrackerAdapter : IWorkTrackerAdapter
{
    public string TrackerKey => "Fake";

    public List<(WorkItemType Type, string Title, WorkItemRef? Parent)> Creates { get; } = [];
    public List<(WorkItemRef Item, IReadOnlyDictionary<string, object?> Fields)> FieldSets { get; } = [];
    public WorkItemRef? ResolveResult { get; set; }

    private int _nextId = 5000;

    public Task<WorkItemRef> CreateWorkItemAsync(
        WorkItemType type, string title, string description, WorkItemRef? parent, CancellationToken ct = default)
    {
        Creates.Add((type, title, parent));
        return Task.FromResult(WorkItemRef.From(_nextId++));
    }

    public Task UpsertWorkItemAsync(WorkItemRef item, string title, string description, string appendComment, CancellationToken ct = default)
        => Task.CompletedTask;

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
