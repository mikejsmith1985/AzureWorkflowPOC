// Hand-rolled fakes shared across the phase-handler unit tests (no I/O, no network).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using System.Collections.Concurrent;

namespace DBAIAzure.Tests.Fakes;

/// <summary>In-memory <see cref="IArtifactReader"/> returning canned artifacts per directory.</summary>
public sealed class FakeArtifactReader : IArtifactReader
{
    private readonly IReadOnlyList<PhaseArtifact> _artifacts;

    public FakeArtifactReader(IReadOnlyList<PhaseArtifact> artifacts) => _artifacts = artifacts;

    public Task<IReadOnlyList<PhaseArtifact>> ReadArtifactsAsync(
        string featureDirectory, CancellationToken cancellationToken = default)
        => Task.FromResult(_artifacts);
}

/// <summary>Records the structured prompt and returns a pre-set validation result — no LLM call.</summary>
public sealed class FakeStructuredCompletionService : IStructuredCompletionService
{
    private readonly PhaseValidationResult _result;

    /// <summary>The user message captured from the last call (lets tests assert the artifacts were sent).</summary>
    public string? LastUserMessage { get; private set; }

    public FakeStructuredCompletionService(PhaseValidationResult result) => _result = result;

    public Task<T> GetStructuredAsync<T>(
        string systemPrompt, string userMessage, string toolName, string toolDescription,
        string inputSchemaJson, CancellationToken cancellationToken = default)
    {
        LastUserMessage = userMessage;
        return Task.FromResult((T)(object)_result!);
    }
}

/// <summary>
/// In-memory <see cref="IBoardsClient"/> recording every create/upsert/comment so tests can assert
/// what was written (and that nothing was written before approval).
/// </summary>
public sealed class FakeBoardsClient : IBoardsClient
{
    private int _nextId = 1000;

    /// <summary>Every create call's arguments, in order.</summary>
    public List<(string Type, string Title, string Description, int? ParentId)> Creates { get; } = [];

    /// <summary>Every upsert call's arguments, in order.</summary>
    public List<(int Id, string Title, string Description, string Comment)> Upserts { get; } = [];

    /// <summary>Every appended comment, in order.</summary>
    public List<(int Id, string Comment)> Comments { get; } = [];

    /// <summary>Every field-update call's arguments, in order (telemetry write-back).</summary>
    public List<(int Id, IReadOnlyDictionary<string, object?> Fields)> FieldUpdates { get; } = [];

    /// <summary>When set, every call throws this to simulate a board-write failure (FR-015).</summary>
    public Exception? FailWith { get; set; }

    public Task<CreatedWorkItemRef> CreateWorkItemAsync(
        string workItemType, string title, string description, int? parentId,
        CancellationToken cancellationToken = default)
    {
        if (FailWith is not null) throw FailWith;
        Creates.Add((workItemType, title, description, parentId));
        var id = _nextId++;
        return Task.FromResult(new CreatedWorkItemRef
        {
            WorkItemId = id,
            WorkItemType = workItemType,
            Url = $"https://dev.azure.com/org/proj/_workitems/edit/{id}",
            WasUpdated = false,
        });
    }

    public Task<CreatedWorkItemRef> UpsertWorkItemAsync(
        int workItemId, string title, string description, string appendComment,
        CancellationToken cancellationToken = default)
    {
        if (FailWith is not null) throw FailWith;
        Upserts.Add((workItemId, title, description, appendComment));
        return Task.FromResult(new CreatedWorkItemRef
        {
            WorkItemId = workItemId,
            WorkItemType = PhaseWorkItemMap.EpicType,
            Url = $"https://dev.azure.com/org/proj/_workitems/edit/{workItemId}",
            WasUpdated = true,
        });
    }

    public Task AppendDiscussionCommentAsync(
        int workItemId, string comment, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null) throw FailWith;
        Comments.Add((workItemId, comment));
        return Task.CompletedTask;
    }

    public Task UpdateFieldsAsync(
        int workItemId, IReadOnlyDictionary<string, object?> fields, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null) throw FailWith;
        FieldUpdates.Add((workItemId, fields));
        return Task.CompletedTask;
    }
}

/// <summary>Captures notifier invocations so the orchestrator's pause-time push can be asserted.</summary>
public sealed class FakePhaseApprovalNotifier : IPhaseApprovalNotifier
{
    public List<(string RunId, SpecKitPhase Phase, string Summary, IReadOnlyList<PhaseValidationGap> Gaps, string PortalUrl)> Notifications { get; } = [];

    public Task NotifyAsync(
        string runId, string featureKey, SpecKitPhase phase, string summary,
        IReadOnlyList<PhaseValidationGap> gaps, string portalUrl, CancellationToken cancellationToken = default)
    {
        Notifications.Add((runId, phase, summary, gaps, portalUrl));
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory <see cref="IPhaseRunRepository"/> for idempotency/lookup assertions. Mirrors the real
/// SQLite repository's idempotency contract: a single record per (FeatureKey, Phase) — a repeat
/// signal updates the existing one rather than adding a duplicate (FR-013).
/// </summary>
public sealed class FakePhaseRunRepository : IPhaseRunRepository
{
    private readonly ConcurrentDictionary<string, PhaseHandlerState> _byFeaturePhase = new();

    /// <summary>Builds the single idempotency key used to collapse repeat signals.</summary>
    private static string KeyOf(string featureKey, SpecKitPhase phase) => $"{featureKey}|{phase}";

    public Task UpsertRunAsync(PhaseHandlerState state, CancellationToken cancellationToken = default)
    {
        var key = KeyOf(state.FeatureKey, state.Phase);

        // Preserve prior work item ids while a repeat run is still in progress (no items of its own
        // yet), so the idempotency anchor survives until the create step reads it — matching the real
        // SQLite repository's behaviour (FR-013).
        if (state.CreatedWorkItems.Count == 0 &&
            _byFeaturePhase.TryGetValue(key, out var prior) && prior.CreatedWorkItems.Count > 0)
        {
            state = state with { CreatedWorkItems = prior.CreatedWorkItems };
        }

        _byFeaturePhase[key] = state;
        return Task.CompletedTask;
    }

    public Task<PhaseRunRecordView?> GetByRunIdAsync(string runId, CancellationToken cancellationToken = default)
    {
        var match = _byFeaturePhase.Values.FirstOrDefault(s => s.RunId == runId);
        return Task.FromResult(match is null ? null : ToView(match));
    }

    public Task<PhaseRunRecordView?> GetByFeaturePhaseAsync(
        string featureKey, SpecKitPhase phase, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            _byFeaturePhase.TryGetValue(KeyOf(featureKey, phase), out var state) ? ToView(state) : null);
    }

    private static PhaseRunRecordView ToView(PhaseHandlerState state) => new()
    {
        RunId = state.RunId,
        FeatureKey = state.FeatureKey,
        Phase = state.Phase,
        Status = state.Status,
        CreatedWorkItems = state.CreatedWorkItems,
    };
}
