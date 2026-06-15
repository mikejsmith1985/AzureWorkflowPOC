// IBoardsClient implementation over the official Azure DevOps Work Item Tracking client (PAT auth).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

namespace DBAIAzure.Web.Integrations.AzureDevOps;

/// <summary>
/// Creates and updates Azure DevOps Boards work items using the official
/// <see cref="WorkItemTrackingHttpClient"/> behind the project's <see cref="IBoardsClient"/> seam.
/// Authenticates with a PAT via <see cref="VssBasicCredential"/>. This is the one piece of custom
/// infrastructure (a genuine external-system gap, per research.md §1) — isolated here so the SK
/// steps never see SDK types. Failures surface as exceptions; the calling step records them (FR-015).
/// </summary>
public sealed class AzureDevOpsBoardsClient : IBoardsClient, IDisposable
{
    // Reference field names + relation type (research.md §2 verified API facts).
    private const string TitleField = "/fields/System.Title";
    private const string DescriptionField = "/fields/System.Description";
    private const string HistoryField = "/fields/System.History";
    private const string AreaPathField = "/fields/System.AreaPath";
    private const string IterationPathField = "/fields/System.IterationPath";
    private const string ParentRelation = "System.LinkTypes.Hierarchy-Reverse";

    private readonly AzureDevOpsOptions _options;
    private readonly VssConnection _connection;
    private readonly WorkItemTrackingHttpClient _client;

    public AzureDevOpsBoardsClient(IOptions<AzureDevOpsOptions> options)
    {
        _options = options.Value;
        var credentials = new VssBasicCredential(string.Empty, _options.Pat);
        _connection = new VssConnection(new Uri(_options.OrganizationUrl), credentials);
        _client = _connection.GetClient<WorkItemTrackingHttpClient>();
    }

    public async Task<CreatedWorkItemRef> CreateWorkItemAsync(
        string workItemType, string title, string description, int? parentId,
        CancellationToken cancellationToken = default)
    {
        var patch = new JsonPatchDocument
        {
            AddField(TitleField, title),
            AddField(DescriptionField, description),
        };

        AppendOptionalPaths(patch);

        // Parent link (child → Epic): a Hierarchy-Reverse relation points at the parent (FR-012).
        if (parentId is { } parent)
            patch.Add(BuildParentRelation(parent));

        // The work item TYPE is the string parameter — never a System.WorkItemType field patch.
        var created = await _client.CreateWorkItemAsync(
            patch, _options.Project, workItemType, cancellationToken: cancellationToken);

        return ToRef(created, workItemType, wasUpdated: false);
    }

    public async Task<CreatedWorkItemRef> UpsertWorkItemAsync(
        int workItemId, string title, string description, string appendComment,
        CancellationToken cancellationToken = default)
    {
        // Refresh fields and APPEND a discussion comment in one update; System.History appends,
        // never overwrites, and each update creates a new immutable revision (FR-013 / FR-018).
        var patch = new JsonPatchDocument
        {
            AddField(TitleField, title),
            AddField(DescriptionField, description),
            AddField(HistoryField, appendComment),
        };

        var updated = await _client.UpdateWorkItemAsync(
            patch, workItemId, cancellationToken: cancellationToken);

        var type = updated.Fields.TryGetValue("System.WorkItemType", out var value)
            ? value?.ToString() ?? string.Empty
            : string.Empty;

        return ToRef(updated, type, wasUpdated: true);
    }

    public async Task AppendDiscussionCommentAsync(
        int workItemId, string comment, CancellationToken cancellationToken = default)
    {
        var patch = new JsonPatchDocument { AddField(HistoryField, comment) };
        await _client.UpdateWorkItemAsync(patch, workItemId, cancellationToken: cancellationToken);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>Adds the optional area/iteration paths when configured.</summary>
    private void AppendOptionalPaths(JsonPatchDocument patch)
    {
        if (!string.IsNullOrWhiteSpace(_options.AreaPath))
            patch.Add(AddField(AreaPathField, _options.AreaPath));
        if (!string.IsNullOrWhiteSpace(_options.IterationPath))
            patch.Add(AddField(IterationPathField, _options.IterationPath));
    }

    /// <summary>Builds a single Add field operation.</summary>
    private static JsonPatchOperation AddField(string path, string value) => new()
    {
        Operation = Operation.Add,
        Path = path,
        Value = value,
    };

    /// <summary>Builds the Hierarchy-Reverse relation linking the new item to its parent Epic.</summary>
    private JsonPatchOperation BuildParentRelation(int parentId) => new()
    {
        Operation = Operation.Add,
        Path = "/relations/-",
        Value = new
        {
            rel = ParentRelation,
            url = $"{_options.OrganizationUrl}/_apis/wit/workItems/{parentId}",
        },
    };

    /// <summary>Projects an SDK work item into the project's <see cref="CreatedWorkItemRef"/>.</summary>
    private static CreatedWorkItemRef ToRef(WorkItem workItem, string workItemType, bool wasUpdated) => new()
    {
        WorkItemId = workItem.Id ?? 0,
        WorkItemType = workItemType,
        Url = workItem.Url ?? string.Empty,
        WasUpdated = wasUpdated,
    };

    public void Dispose() => _connection.Dispose();
}
