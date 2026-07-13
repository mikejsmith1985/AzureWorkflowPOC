// Framework-neutral board-write logic for the phase-handler pipeline (spec-019): the approved work-item
// creation/upsert + cost/telemetry write-back, shared by the SK CreateWorkItemStep and the MAF
// CreateWorkItemExecutor so both frameworks write the board identically (parity — FR-015).
using System.Text;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.AdoTelemetry;
using DBAIAzure.Core.Models.WorkTracker;

namespace DBAIAzure.Processes.Pipeline;

/// <summary>
/// The dependencies the board write needs, resolved once by the caller from its own container (SK
/// <c>kernel.Services</c> or the MAF executor's service bag). Every field except the tracker is optional;
/// a null service degrades gracefully exactly as the original step did (best-effort cost/telemetry).
/// </summary>
/// <param name="Tracker">The work-tracker adapter that performs the board create/upsert (required to write).</param>
/// <param name="Repository">Phase-run store for idempotency + hierarchy lookups; a null store is treated as empty.</param>
/// <param name="BindingMap">Binding-key → work-item map for dev-usage ingest (spec-017); optional.</param>
/// <param name="Ledger">Runtime-cost ledger; optional.</param>
/// <param name="TelemetrySource">Run telemetry aggregate source for cost estimation; optional.</param>
/// <param name="Projection">Cost-field projection for the tracker's native rollup; optional.</param>
/// <param name="WriteBack">Per-item token snapshot write-back (spec-016); optional.</param>
public readonly record struct PhaseWorkItemWriterDeps(
    IWorkTrackerAdapter Tracker,
    IPhaseRunRepository? Repository = null,
    IBindingWorkItemMap? BindingMap = null,
    ICostLedger? Ledger = null,
    IRunTelemetrySource? TelemetrySource = null,
    ICostProjection? Projection = null,
    ITelemetryWriteBack? WriteBack = null);

/// <summary>
/// Writes the tracking work item(s) to the board, gated on an approved <see cref="ApprovalDecision"/>, and
/// returns the resulting <see cref="PhaseHandlerState"/> (never throws for a board-write failure — it is
/// recorded as <see cref="PhaseRunStatus.Failed"/> with the approval preserved, FR-015). Specify creates one
/// Epic; Plan creates one Task per planned unit of work (linked under the feature's Epic, auto-created if
/// missing — FR-012); Implement creates one Bug/completion record. A repeat (feature, phase) signal upserts
/// the existing work item instead of duplicating it (FR-013). Extracted verbatim from the SK
/// <c>CreateWorkItemStep</c> so both frameworks produce identical board state.
/// </summary>
public static class PhaseWorkItemWriter
{
    /// <summary>Creates or upserts the work item(s) if approved; returns a rejected/unsupported/failed state otherwise.</summary>
    public static async Task<PhaseHandlerState> WriteAsync(PhaseHandlerState state, PhaseWorkItemWriterDeps deps)
    {
        // Guard: never write without a recorded approval (FR-006).
        if (state.Decision is not { IsApproved: true })
        {
            return state with { Status = PhaseRunStatus.Rejected };
        }

        var workItemType = PhaseWorkItemMap.ToWorkItemType(state.Phase);
        if (workItemType is null)
        {
            // Defensive: an unsupported phase should not reach this point, but never write if it does.
            return state with { Status = PhaseRunStatus.Unsupported };
        }

        var repository = deps.Repository ?? NullPhaseRunRepository.Instance;

        try
        {
            var created = state.Phase == SpecKitPhase.Plan
                ? await WritePlanTasksAsync(state, deps.Tracker, repository)
                : [await WriteSingleItemAsync(state, workItemType, deps.Tracker, repository)];

            // Best-effort: stamp the run's AI telemetry onto the work item(s) just written. Never let a
            // telemetry failure undo an approved board write (FR-015 spirit) — the work item stands.
            await WriteTelemetryAsync(deps, state, created);

            return state with { Status = PhaseRunStatus.Completed, CreatedWorkItems = created };
        }
        catch (Exception ex)
        {
            // FR-015: record and surface the board-write failure; the approval is preserved in state.
            return state with
            {
                Status = PhaseRunStatus.Failed,
                FailureReason = $"Board write failed after approval: {ex.Message}",
            };
        }
    }

    /// <summary>
    /// Writes the run's aggregated AI telemetry onto each created work item, if the write-back services are
    /// present. Entirely best-effort: any failure is swallowed so an approved board write is never undone by
    /// a telemetry problem.
    /// </summary>
    private static async Task WriteTelemetryAsync(
        PhaseWorkItemWriterDeps deps, PhaseHandlerState state, IReadOnlyList<CreatedWorkItemRef> created)
    {
        if (created.Count == 0)
            return;

        var repository = deps.Repository ?? NullPhaseRunRepository.Instance;
        var anchor = await ResolveAnchorRefAsync(state, created, repository);

        // spec-017 US1: stamp the binding key on every created item + record the binding→work-item map
        // (keyed to the cost anchor) so dev-usage ingest resolves the key. Best-effort — a cost/telemetry
        // failure never undoes an approved board write (FR-011).
        if (!string.IsNullOrWhiteSpace(state.CostBindingKey))
        {
            foreach (var workItem in created)
            {
                try
                {
                    await deps.Tracker.SetFieldsAsync(workItem.WorkItemId,
                        new Dictionary<string, object?> { [LogicalField.CostBindingKey] = state.CostBindingKey });
                }
                catch { /* binding stamp is best-effort */ }
            }

            if (deps.BindingMap is not null)
            {
                try { await deps.BindingMap.PutAsync(state.CostBindingKey!, anchor); }
                catch { /* map population is best-effort */ }
            }

            // spec-017 US2: append ONE runtime cost entry on the anchor (no per-child duplication, FR-008)
            // then project the cumulative cost fields for the tracker's native rollup.
            await AppendRuntimeCostAsync(deps, state, anchor);
        }

        // Per-item token snapshot (spec-016) — ADO-specific; recorded for numeric refs (Jira snapshot is a follow-up).
        if (deps.WriteBack is not null)
        {
            foreach (var workItem in created)
            {
                if (!workItem.WorkItemId.TryAsInt(out var numericId))
                    continue;
                try
                {
                    await deps.WriteBack.WriteBackAsync(new TelemetryWriteBackRequest(
                        state.RunId, workItem.WorkItemType, numericId, state.Phase.ToString(),
                        // Attribute to the signal's requester; fall back to the approver when none was given (#44).
                        TriggeredBy: state.TriggeredBy ?? state.Decision?.DecidedBy));
                }
                catch { /* token snapshot is non-essential */ }
            }
        }
    }

    /// <summary>
    /// The cost anchor: the feature's Epic for a Plan run (planning cost belongs to the feature, not an
    /// arbitrary Task), otherwise the single created item. Read-only — the Epic already exists by now.
    /// </summary>
    private static async Task<WorkItemRef> ResolveAnchorRefAsync(
        PhaseHandlerState state, IReadOnlyList<CreatedWorkItemRef> created, IPhaseRunRepository repository)
    {
        if (state.Phase == SpecKitPhase.Plan)
        {
            var specifyRun = await repository.GetByFeaturePhaseAsync(state.FeatureKey, SpecKitPhase.Specify);
            var epic = specifyRun?.CreatedWorkItems.FirstOrDefault(i => i.WorkItemType == PhaseWorkItemMap.EpicType);
            if (epic is not null)
                return epic.WorkItemId;
        }
        return created[0].WorkItemId;
    }

    /// <summary>Appends the run's single runtime cost entry on the anchor, then projects the totals.</summary>
    private static async Task AppendRuntimeCostAsync(PhaseWorkItemWriterDeps deps, PhaseHandlerState state, WorkItemRef anchor)
    {
        if (deps.Ledger is null)
            return;

        try
        {
            var aggregate = deps.TelemetrySource is not null
                ? await deps.TelemetrySource.GetAggregateAsync(state.RunId)
                : RunTelemetryAggregate.Empty(state.RunId);

            var cost = ModelPricing.EstimateCostUsd(
                aggregate.ModelName, aggregate.InputTokens, aggregate.OutputTokens,
                aggregate.CacheReadTokens, aggregate.CacheCreationTokens) ?? 0;

            await deps.Ledger.AppendAsync(new CostLedgerEntry
            {
                Id = Guid.NewGuid(),
                BindingKey = state.CostBindingKey!,
                Dimension = CostDimension.Runtime,
                WorkItemId = anchor.Value,
                ModelName = aggregate.ModelName,
                InputTokens = aggregate.InputTokens,
                OutputTokens = aggregate.OutputTokens,
                CacheReadTokens = aggregate.CacheReadTokens,
                CostUsd = cost,
                OccurredAt = DateTimeOffset.UtcNow,
                SourceId = state.RunId,
            });

            if (deps.Projection is not null)
                await deps.Projection.ProjectAsync(state.CostBindingKey!, anchor);
        }
        catch { /* runtime cost capture is best-effort (FR-011) */ }
    }

    // ── Per-phase write strategies ────────────────────────────────────────────────

    /// <summary>
    /// Plan phase: creates (or upserts) one Task per planned unit of work, each linked under the
    /// feature's Epic. The Epic is auto-created first when the feature has none (FR-008, FR-012).
    /// </summary>
    private static async Task<IReadOnlyList<CreatedWorkItemRef>> WritePlanTasksAsync(
        PhaseHandlerState state, IWorkTrackerAdapter tracker, IPhaseRunRepository repository)
    {
        var epicRef = await ResolveOrCreateEpicRefAsync(state, tracker, repository);
        var existing = await LoadExistingWorkItemsAsync(state, repository);
        var created = new List<CreatedWorkItemRef>(state.PlannedItems.Count);

        for (var index = 0; index < state.PlannedItems.Count; index++)
        {
            var plannedItem = state.PlannedItems[index];
            var title = BuildPlannedTaskTitle(state, plannedItem);
            var description = plannedItem.Description;

            // Upsert the matching previously-created Task (positional) when re-signalled; else create.
            var priorRef = index < existing.Count ? existing[index] : null;
            created.Add(priorRef is null
                ? await tracker.CreateWorkItemAsync(WorkItemType.Task, title, description, epicRef)
                : await tracker.UpsertWorkItemAsync(
                    priorRef.WorkItemId, title, description, BuildUpsertComment(state)));
        }

        return created;
    }

    /// <summary>
    /// Specify/Implement phase: creates (or upserts) a single work item. The Specify Epic is a
    /// top-level item; the Implement Bug is linked under the feature's Epic (auto-created if missing).
    /// </summary>
    private static async Task<CreatedWorkItemRef> WriteSingleItemAsync(
        PhaseHandlerState state, string workItemType, IWorkTrackerAdapter tracker, IPhaseRunRepository repository)
    {
        var title = BuildTitle(state, workItemType);
        var description = BuildDescription(state);

        // Idempotent upsert: a repeat (feature, phase) signal refreshes the existing item (FR-013).
        var existing = await LoadExistingWorkItemsAsync(state, repository);
        if (existing.Count > 0)
        {
            return await tracker.UpsertWorkItemAsync(
                existing[0].WorkItemId, title, description, BuildUpsertComment(state));
        }

        // Implement children link under the feature's Epic (auto-created if missing); Specify is top-level.
        WorkItemRef? parent = state.Phase == SpecKitPhase.Implement
            ? await ResolveOrCreateEpicRefAsync(state, tracker, repository)
            : null;

        return await tracker.CreateWorkItemAsync(ToLogicalType(workItemType), title, description, parent);
    }

    /// <summary>Maps an ADO work item type name to the logical <see cref="WorkItemType"/>.</summary>
    private static WorkItemType ToLogicalType(string workItemType) =>
        workItemType == PhaseWorkItemMap.EpicType ? WorkItemType.Epic
        : workItemType == PhaseWorkItemMap.BugType ? WorkItemType.Bug
        : WorkItemType.Task;

    // ── Hierarchy + idempotency lookups (FR-012 / FR-013) ─────────────────────────

    /// <summary>
    /// Finds the feature's Epic id via the stored Specify-phase run; if no Epic exists yet, creates
    /// one first so a child is never orphaned (FR-012). Returns the Epic's work item id.
    /// </summary>
    private static async Task<WorkItemRef> ResolveOrCreateEpicRefAsync(
        PhaseHandlerState state, IWorkTrackerAdapter tracker, IPhaseRunRepository repository)
    {
        var specifyRun = await repository.GetByFeaturePhaseAsync(state.FeatureKey, SpecKitPhase.Specify);
        var epicRef = specifyRun?.CreatedWorkItems
            .FirstOrDefault(item => item.WorkItemType == PhaseWorkItemMap.EpicType);
        if (epicRef is not null) return epicRef.WorkItemId;

        // No Epic yet: create a minimal one from this feature so the hierarchy is intact (no orphans).
        var epicTitle = $"[{PhaseWorkItemMap.EpicType}] {state.FeatureKey}";
        var epic = await tracker.CreateWorkItemAsync(
            WorkItemType.Epic, epicTitle, BuildDescription(state), parent: null);

        // Persist the auto-created Epic under a synthetic Specify run so a subsequent Specify signal
        // finds it and upserts rather than creating a duplicate Epic (FR-013 idempotency).
        var syntheticSpecifyState = state with
        {
            RunId           = $"auto-{Guid.NewGuid():N}"[..16],
            Phase           = SpecKitPhase.Specify,
            Status          = PhaseRunStatus.Completed,
            Validation      = null,
            Decision        = null,
            FailureReason   = null,
            CreatedWorkItems = [epic],
        };
        await repository.UpsertRunAsync(syntheticSpecifyState);

        return epic.WorkItemId;
    }

    /// <summary>
    /// Loads the work items previously created for this (feature, phase), or an empty list. The mere
    /// presence of stored work item ids — not the run's current status — is the idempotency anchor: a
    /// repeat signal finds them and upserts instead of creating duplicates (FR-013).
    /// </summary>
    private static async Task<IReadOnlyList<CreatedWorkItemRef>> LoadExistingWorkItemsAsync(
        PhaseHandlerState state, IPhaseRunRepository repository)
    {
        var priorRun = await repository.GetByFeaturePhaseAsync(state.FeatureKey, state.Phase);
        return priorRun?.CreatedWorkItems ?? [];
    }

    // ── Field derivation (content from artifacts, never invented — FR-009) ─────────

    /// <summary>Derives the primary work item title from the feature and phase.</summary>
    private static string BuildTitle(PhaseHandlerState state, string workItemType) =>
        state.Phase == SpecKitPhase.Specify
            ? $"[{workItemType}] {state.FeatureKey}"
            : $"[{workItemType}] {state.FeatureKey} — {state.Phase}";

    /// <summary>Derives a Plan child Task title from a parsed planned unit of work.</summary>
    private static string BuildPlannedTaskTitle(PhaseHandlerState state, PlannedWorkItem plannedItem) =>
        $"[{PhaseWorkItemMap.TaskType}] {state.FeatureKey} — {plannedItem.Title}";

    /// <summary>Derives the work item description from the validation summary and flagged gaps.</summary>
    private static string BuildDescription(PhaseHandlerState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine(state.Validation?.Summary ?? "No summary available.");

        var gaps = state.Validation?.Gaps ?? [];
        if (gaps.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Flagged gaps:");
            foreach (var gap in gaps)
                builder.AppendLine($"- {gap.Label}: {gap.Description}");
        }

        if (!string.IsNullOrWhiteSpace(state.Decision?.Note))
        {
            builder.AppendLine();
            builder.AppendLine($"Reviewer note: {state.Decision!.Note}");
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>
    /// Builds the timestamped Discussion comment appended on a re-signal upsert: the latest summary
    /// plus flagged gaps, so each work item carries an auditable re-validation trail (FR-013 / FR-018).
    /// </summary>
    private static string BuildUpsertComment(PhaseHandlerState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Re-validated {state.Phase} at {DateTimeOffset.UtcNow:u}.");
        builder.AppendLine(state.Validation?.Summary ?? "No summary available.");

        var gaps = state.Validation?.Gaps ?? [];
        if (gaps.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Flagged gaps:");
            foreach (var gap in gaps)
                builder.AppendLine($"- {gap.Label}: {gap.Description}");
        }

        return builder.ToString().TrimEnd();
    }
}
