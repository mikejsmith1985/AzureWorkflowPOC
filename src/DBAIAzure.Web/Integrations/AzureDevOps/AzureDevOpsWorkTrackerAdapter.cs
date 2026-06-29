// Azure DevOps implementation of IWorkTrackerAdapter — delegates to the existing ADO client + preflight
// so the live ADO behaviour is unchanged (spec-018, SC-001). Converts WorkItemRef<->int at the boundary.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DBAIAzure.Web.Integrations.AzureDevOps;

/// <summary>
/// Wraps <see cref="IBoardsClient"/> (create/upsert/comment/set-fields), <see cref="IBindingWorkItemMap"/>
/// (binding resolution), and <see cref="IAdoTelemetryPreflightService"/> (field provisioning) behind the
/// tracker-neutral contract. ADO behaviour — including the inherited-process field handling — is untouched;
/// only the caller indirection changes.
/// </summary>
public sealed class AzureDevOpsWorkTrackerAdapter : IWorkTrackerAdapter
{
    private readonly IBoardsClient _boards;
    private readonly IBindingWorkItemMap _bindingMap;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AdoFieldReferenceResolver _fieldResolver;
    private readonly ILogger<AzureDevOpsWorkTrackerAdapter> _logger;

    // The create/set/resolve path uses only singletons, so this adapter is itself a singleton. The ADO
    // preflight (provisioning) is scoped, so it is resolved on demand from a fresh scope rather than held
    // as a field — this is what lets the adapter be injected into the per-run phase kernel.
    public AzureDevOpsWorkTrackerAdapter(
        IBoardsClient boards, IBindingWorkItemMap bindingMap, IServiceScopeFactory scopeFactory,
        AdoFieldReferenceResolver fieldResolver, ILogger<AzureDevOpsWorkTrackerAdapter> logger)
    {
        _boards = boards;
        _bindingMap = bindingMap;
        _scopeFactory = scopeFactory;
        _fieldResolver = fieldResolver;
        _logger = logger;
    }

    public string TrackerKey => "AzureDevOps";

    /// <inheritdoc/>
    public Task<CreatedWorkItemRef> CreateWorkItemAsync(
        WorkItemType type, string title, string description, WorkItemRef? parent, CancellationToken ct = default)
    {
        int? parentId = parent is { } p && p.TryAsInt(out var pid) ? pid : null;
        return _boards.CreateWorkItemAsync(ToAdoTypeName(type), title, description, parentId, ct);
    }

    /// <inheritdoc/>
    public async Task<CreatedWorkItemRef> UpsertWorkItemAsync(
        WorkItemRef item, string title, string description, string appendComment, CancellationToken ct = default)
    {
        if (!item.TryAsInt(out var id))
            throw new InvalidOperationException($"Azure DevOps work item ref '{item.Value}' is not numeric.");
        return await _boards.UpsertWorkItemAsync(id, title, description, appendComment, ct);
    }

    /// <inheritdoc/>
    public async Task AppendCommentAsync(WorkItemRef item, string comment, CancellationToken ct = default)
    {
        if (item.TryAsInt(out var id))
            await _boards.AppendDiscussionCommentAsync(id, comment, ct);
    }

    /// <inheritdoc/>
    public async Task SetFieldsAsync(
        WorkItemRef item, IReadOnlyDictionary<string, object?> logicalFields, CancellationToken ct = default)
    {
        if (!item.TryAsInt(out var id) || logicalFields.Count == 0)
            return;

        var native = logicalFields.ToDictionary(
            kv => _fieldResolver.ToNativeReference(kv.Key), kv => kv.Value);
        await _boards.UpdateFieldsAsync(id, native, ct);
    }

    /// <inheritdoc/>
    public Task<WorkItemRef?> ResolveByBindingKeyAsync(string bindingKey, CancellationToken ct = default) =>
        _bindingMap.ResolveAsync(bindingKey, ct);

    /// <inheritdoc/>
    public async Task<ProvisioningResult> ProvisionFieldsAsync(
        AdoTelemetryFieldConfig fieldConfig, CancellationToken ct = default)
    {
        // Resolve the scoped preflight from a fresh scope (the adapter itself is a singleton).
        await using var scope = _scopeFactory.CreateAsyncScope();
        var preflight = scope.ServiceProvider.GetRequiredService<IAdoTelemetryPreflightService>();
        var result = await preflight.RunPreflightAsync(fieldConfig, ct);
        if (!result.IsSuccess)
            return new ProvisioningResult
            {
                IsSuccess = false,
                Mode = "Failed",
                FieldsFailed = [new FieldProvisioningFailure("(preflight)", result.ErrorMessage ?? "preflight failed")],
            };

        var ready = new List<string>();
        var failed = new List<FieldProvisioningFailure>();
        if (result.Manifest is BootstrapManifest bootstrap)
        {
            ready.AddRange(bootstrap.FieldsCreated);
            ready.AddRange(bootstrap.FieldsExisting);
            failed.AddRange(bootstrap.FieldsFailed.Select(f => new FieldProvisioningFailure(f.ReferenceName, f.Error)));
        }

        return new ProvisioningResult
        {
            IsSuccess = true,
            Mode = result.Manifest?.Mode.ToString() ?? "Unknown",
            FieldsReady = ready,
            FieldsFailed = failed,
        };
    }

    /// <inheritdoc/>
    public RollupCapability GetRollupCapability() =>
        new(RollupKind.Native, NativeTool: "ADO Analytics");

    // Logical type -> Azure DevOps work item type display name (Agile process).
    private static string ToAdoTypeName(WorkItemType type) => type switch
    {
        WorkItemType.Epic => "Epic",
        WorkItemType.UserStory => "User Story",
        WorkItemType.Task => "Task",
        WorkItemType.Bug => "Bug",
        _ => "Task",
    };
}
