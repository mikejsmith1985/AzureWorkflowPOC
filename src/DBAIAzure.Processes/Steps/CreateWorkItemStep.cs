// SK step: creates the board work item(s) — but only on an approved decision (FR-006), recording
// a board-write failure rather than discarding the approval (FR-015).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using System.Text;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Writes the tracking work item(s) to the board, gated on an approved <see cref="ApprovalDecision"/>.
/// Specify creates one Epic; Plan creates one Task per planned unit of work; Implement creates one
/// Bug/completion record. Plan/Implement children are linked under the feature's Epic, auto-creating
/// the Epic first if it does not yet exist so nothing is orphaned (FR-012). A repeat (feature, phase)
/// signal upserts the existing work item instead of duplicating it (FR-013). A board-write failure is
/// recorded as <see cref="PhaseRunStatus.Failed"/> with the approval preserved (FR-015) — never swallowed.
/// </summary>
public sealed class CreateWorkItemStep : KernelProcessStep
{
    /// <summary>Creates or upserts the work item(s) if approved; rejects, fails, or no-ops otherwise.</summary>
    [KernelFunction]
    public async Task CreateAsync(KernelProcessStepContext ctx, PhaseHandlerState state, Kernel kernel)
    {
        var sink = kernel.Services.GetService<IPhaseProgressSink>();

        // Guard: never write without a recorded approval (FR-006).
        if (state.Decision is not { IsApproved: true })
        {
            await EmitAsync(ctx, sink, state with { Status = PhaseRunStatus.Rejected },
                PhaseHandlerEvents.WorkItemWritten);
            return;
        }

        var workItemType = PhaseWorkItemMap.ToWorkItemType(state.Phase);
        if (workItemType is null)
        {
            // Defensive: an unsupported phase should not reach this step, but never write if it does.
            await EmitAsync(ctx, sink, state with { Status = PhaseRunStatus.Unsupported },
                PhaseHandlerEvents.Unsupported);
            return;
        }

        var boardsClient = kernel.GetRequiredService<IBoardsClient>();
        var repository = kernel.Services.GetService<IPhaseRunRepository>() ?? NullPhaseRunRepository.Instance;

        try
        {
            var created = state.Phase == SpecKitPhase.Plan
                ? await WritePlanTasksAsync(state, boardsClient, repository)
                : [await WriteSingleItemAsync(state, workItemType, boardsClient, repository)];

            // Best-effort: stamp the run's AI telemetry onto the work item(s) just written. Never let a
            // telemetry failure undo an approved board write (FR-015 spirit) — the work item stands.
            await WriteTelemetryAsync(kernel, state, created);

            await EmitAsync(ctx, sink,
                state with { Status = PhaseRunStatus.Completed, CreatedWorkItems = created },
                PhaseHandlerEvents.WorkItemWritten);
        }
        catch (Exception ex)
        {
            // FR-015: record and surface the board-write failure; the approval is preserved in state.
            await EmitAsync(ctx, sink,
                state with
                {
                    Status = PhaseRunStatus.Failed,
                    FailureReason = $"Board write failed after approval: {ex.Message}",
                },
                PhaseHandlerEvents.Failed);
        }
    }

    /// <summary>
    /// Writes the run's aggregated AI telemetry onto each created work item, if a telemetry write-back
    /// service is registered. Entirely best-effort: any failure is swallowed so an approved board write
    /// is never undone by a telemetry problem. The service itself also returns (never throws) on errors.
    /// </summary>
    private static async Task WriteTelemetryAsync(
        Kernel kernel, PhaseHandlerState state, IReadOnlyList<CreatedWorkItemRef> created)
    {
        var writeBack = kernel.Services.GetService<ITelemetryWriteBack>();
        if (writeBack is null)
            return;

        foreach (var workItem in created)
        {
            try
            {
                await writeBack.WriteBackAsync(new TelemetryWriteBackRequest(
                    state.RunId, workItem.WorkItemType, workItem.WorkItemId, state.Phase.ToString()));
            }
            catch
            {
                // Telemetry is non-essential; the work item creation already succeeded.
            }
        }
    }

    // ── Per-phase write strategies ────────────────────────────────────────────────

    /// <summary>
    /// Plan phase: creates (or upserts) one Task per planned unit of work, each linked under the
    /// feature's Epic. The Epic is auto-created first when the feature has none (FR-008, FR-012).
    /// </summary>
    private static async Task<IReadOnlyList<CreatedWorkItemRef>> WritePlanTasksAsync(
        PhaseHandlerState state, IBoardsClient boardsClient, IPhaseRunRepository repository)
    {
        var epicId = await ResolveOrCreateEpicIdAsync(state, boardsClient, repository);
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
                ? await boardsClient.CreateWorkItemAsync(PhaseWorkItemMap.TaskType, title, description, epicId)
                : await boardsClient.UpsertWorkItemAsync(
                    priorRef.WorkItemId, title, description, BuildUpsertComment(state)));
        }

        return created;
    }

    /// <summary>
    /// Specify/Implement phase: creates (or upserts) a single work item. The Specify Epic is a
    /// top-level item; the Implement Bug is linked under the feature's Epic (auto-created if missing).
    /// </summary>
    private static async Task<CreatedWorkItemRef> WriteSingleItemAsync(
        PhaseHandlerState state, string workItemType, IBoardsClient boardsClient, IPhaseRunRepository repository)
    {
        var title = BuildTitle(state, workItemType);
        var description = BuildDescription(state);

        // Idempotent upsert: a repeat (feature, phase) signal refreshes the existing item (FR-013).
        var existing = await LoadExistingWorkItemsAsync(state, repository);
        if (existing.Count > 0)
        {
            return await boardsClient.UpsertWorkItemAsync(
                existing[0].WorkItemId, title, description, BuildUpsertComment(state));
        }

        // Implement children link under the feature's Epic (auto-created if missing); Specify is top-level.
        int? parentId = state.Phase == SpecKitPhase.Implement
            ? await ResolveOrCreateEpicIdAsync(state, boardsClient, repository)
            : null;

        return await boardsClient.CreateWorkItemAsync(workItemType, title, description, parentId);
    }

    // ── Hierarchy + idempotency lookups (FR-012 / FR-013) ─────────────────────────

    /// <summary>
    /// Finds the feature's Epic id via the stored Specify-phase run; if no Epic exists yet, creates
    /// one first so a child is never orphaned (FR-012). Returns the Epic's work item id.
    /// </summary>
    private static async Task<int> ResolveOrCreateEpicIdAsync(
        PhaseHandlerState state, IBoardsClient boardsClient, IPhaseRunRepository repository)
    {
        var specifyRun = await repository.GetByFeaturePhaseAsync(state.FeatureKey, SpecKitPhase.Specify);
        var epicRef = specifyRun?.CreatedWorkItems
            .FirstOrDefault(item => item.WorkItemType == PhaseWorkItemMap.EpicType);
        if (epicRef is not null) return epicRef.WorkItemId;

        // No Epic yet: create a minimal one from this feature so the hierarchy is intact (no orphans).
        var epicTitle = $"[{PhaseWorkItemMap.EpicType}] {state.FeatureKey}";
        var epic = await boardsClient.CreateWorkItemAsync(
            PhaseWorkItemMap.EpicType, epicTitle, BuildDescription(state), parentId: null);

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

    /// <summary>Reports a state to the progress sink and emits the matching process event.</summary>
    private static async Task EmitAsync(
        KernelProcessStepContext ctx, IPhaseProgressSink? sink, PhaseHandlerState state, string eventId)
    {
        sink?.Report(state);
        await ctx.EmitEventAsync(new() { Id = eventId, Data = state });
    }
}
