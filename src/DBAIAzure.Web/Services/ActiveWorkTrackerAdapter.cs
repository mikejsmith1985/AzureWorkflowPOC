// A stable IWorkTrackerAdapter that forwards each call to whichever provider is active right now (spec-020, D3).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;

namespace DBAIAzure.Web.Services;

/// <summary>
/// A routing adapter injected where a component holds a single <see cref="IWorkTrackerAdapter"/> for its whole
/// lifetime (e.g. the singleton phase-handler orchestrator). It resolves the active provider from the connector
/// store on every operation and delegates to that provider's concrete adapter, so switching the active tracker
/// in the UI takes effect on the next call without rebuilding the holder or restarting the app (FR-005). It is
/// deliberately NOT registered as an <see cref="IWorkTrackerAdapter"/> so it never appears in the adapter set it
/// itself routes over.
/// </summary>
public sealed class ActiveWorkTrackerAdapter : IWorkTrackerAdapter
{
    private readonly IReadOnlyList<IWorkTrackerAdapter> _adapters;
    private readonly IWorkTrackerConfigResolver _resolver;

    public ActiveWorkTrackerAdapter(IEnumerable<IWorkTrackerAdapter> adapters, IWorkTrackerConfigResolver resolver)
    {
        _adapters = adapters.ToList();
        _resolver = resolver;
    }

    /// <inheritdoc/>
    public string TrackerKey => ResolveActive().TrackerKey;

    /// <inheritdoc/>
    public async Task<CreatedWorkItemRef> CreateWorkItemAsync(
        WorkItemType type, string title, string description, WorkItemRef? parent, CancellationToken cancellationToken = default) =>
        await (await ResolveActiveAsync(cancellationToken)).CreateWorkItemAsync(type, title, description, parent, cancellationToken);

    /// <inheritdoc/>
    public async Task<CreatedWorkItemRef> UpsertWorkItemAsync(
        WorkItemRef item, string title, string description, string appendComment, CancellationToken cancellationToken = default) =>
        await (await ResolveActiveAsync(cancellationToken)).UpsertWorkItemAsync(item, title, description, appendComment, cancellationToken);

    /// <inheritdoc/>
    public async Task AppendCommentAsync(WorkItemRef item, string comment, CancellationToken cancellationToken = default) =>
        await (await ResolveActiveAsync(cancellationToken)).AppendCommentAsync(item, comment, cancellationToken);

    /// <inheritdoc/>
    public async Task SetFieldsAsync(
        WorkItemRef item, IReadOnlyDictionary<string, object?> logicalFields, CancellationToken cancellationToken = default) =>
        await (await ResolveActiveAsync(cancellationToken)).SetFieldsAsync(item, logicalFields, cancellationToken);

    /// <inheritdoc/>
    public async Task<WorkItemRef?> ResolveByBindingKeyAsync(string bindingKey, CancellationToken cancellationToken = default) =>
        await (await ResolveActiveAsync(cancellationToken)).ResolveByBindingKeyAsync(bindingKey, cancellationToken);

    /// <inheritdoc/>
    public async Task<ProvisioningResult> ProvisionFieldsAsync(
        AdoTelemetryFieldConfig fieldConfig, CancellationToken cancellationToken = default) =>
        await (await ResolveActiveAsync(cancellationToken)).ProvisionFieldsAsync(fieldConfig, cancellationToken);

    /// <inheritdoc/>
    public RollupCapability GetRollupCapability() => ResolveActive().GetRollupCapability();

    private async Task<IWorkTrackerAdapter> ResolveActiveAsync(CancellationToken ct)
    {
        var resolved = await _resolver.ResolveActiveAsync(ct);
        return WorkTrackerAdapterProvider.Select(_adapters, resolved.Provider);
    }

    private IWorkTrackerAdapter ResolveActive() =>
        WorkTrackerAdapterProvider.Select(_adapters, _resolver.ResolveActiveAsync().GetAwaiter().GetResult().Provider);
}
