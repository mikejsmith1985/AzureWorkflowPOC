// The immutable state object that flows through the phase-handler SK process, plus its sub-records.
namespace DBAIAzure.Core.Models;

/// <summary>
/// Lifecycle status of a phase-handler run. Mirrors the state machine in data-model.md:
/// no board write can occur before <see cref="WritingBoard"/>, which is only reachable through
/// an approved decision (enforces FR-006).
/// </summary>
public enum PhaseRunStatus
{
    /// <summary>Signal accepted; run created but artifacts not yet read.</summary>
    Received,

    /// <summary>Artifact files have been read from disk.</summary>
    ArtifactsRead,

    /// <summary>The structured validation summary + gaps have been produced.</summary>
    Validated,

    /// <summary>Paused, waiting for the reviewer's approve/reject decision.</summary>
    AwaitingApproval,

    /// <summary>Approved — currently writing/upserting the work item on the board.</summary>
    WritingBoard,

    /// <summary>Work item created or upserted successfully (terminal).</summary>
    Completed,

    /// <summary>Reviewer rejected; no work item created (terminal).</summary>
    Rejected,

    /// <summary>Phase value outside the supported set; no work item created (terminal).</summary>
    Unsupported,

    /// <summary>A read, validate, or board-write error was recorded (terminal); see FailureReason.</summary>
    Failed,
}

/// <summary>
/// The single immutable state object threaded through the phase-handler SK process. Every step
/// produces a new instance via a <c>with</c> expression — never mutated in place — exactly like
/// <see cref="TicketState"/> in the ticket pipeline.
/// </summary>
public record PhaseHandlerState
{
    /// <summary>Correlates the run across events, persistence, and the approval callback.</summary>
    public required string RunId { get; init; }

    /// <summary>Repo-relative <c>specs/NNN-feature-name/</c> path resolved from the signal.</summary>
    public required string FeatureDirectory { get; init; }

    /// <summary>Stable feature identifier (the <c>NNN-feature-name</c> slug) — idempotency key part 1.</summary>
    public required string FeatureKey { get; init; }

    /// <summary>The completed phase — idempotency key part 2 and the work-item-type driver.</summary>
    public required SpecKitPhase Phase { get; init; }

    /// <summary>Artifact files read for this phase (file name + text content).</summary>
    public IReadOnlyList<PhaseArtifact> Artifacts { get; init; } = [];

    /// <summary>Structured summary + flagged gaps; null until the validation step runs.</summary>
    public PhaseValidationResult? Validation { get; init; }

    /// <summary>The human approve/reject decision; null until the decision card responds.</summary>
    public ApprovalDecision? Decision { get; init; }

    /// <summary>For the Plan phase: the parsed planned units of work (one Task each).</summary>
    public IReadOnlyList<PlannedWorkItem> PlannedItems { get; init; } = [];

    /// <summary>Ids/urls of work items created or updated on the board (empty before approval — FR-006).</summary>
    public IReadOnlyList<CreatedWorkItemRef> CreatedWorkItems { get; init; } = [];

    /// <summary>Current lifecycle status.</summary>
    public PhaseRunStatus Status { get; init; } = PhaseRunStatus.Received;

    /// <summary>Populated on a recorded failure (missing artifacts, board write, etc.).</summary>
    public string? FailureReason { get; init; }

    /// <summary>
    /// Canonical, source-neutral AI-cost binding key minted at intake (spec-017). Carried through the
    /// run, enforced at DoR, written to the work item + ServiceNow ticket, and used to attribute cost.
    /// </summary>
    public string? CostBindingKey { get; init; }

    /// <summary>
    /// Identity that triggered this run (from the phase signal). Attributes the run's AI usage to a
    /// person on the work item. Null when the signal omitted it (the approver is used as a fallback).
    /// </summary>
    public string? TriggeredBy { get; init; }
}

/// <summary>A single artifact file read for the phase.</summary>
public record PhaseArtifact
{
    /// <summary>File name, e.g. <c>spec.md</c>, <c>plan.md</c>, <c>tasks.md</c>.</summary>
    public required string FileName { get; init; }

    /// <summary>UTF-8 file text (bounded by the configured per-file/byte limits).</summary>
    public required string Content { get; init; }
}

/// <summary>A planned unit of work parsed from the Plan phase artifacts; becomes one child Task.</summary>
public record PlannedWorkItem
{
    /// <summary>Title for the child Task.</summary>
    public required string Title { get; init; }

    /// <summary>Body derived from the planned unit of work.</summary>
    public required string Description { get; init; }
}

/// <summary>
/// The recorded human choice that gates any board write. Originates from the reviewer's
/// decision card and is delivered back via the approval callback endpoint.
/// </summary>
public record ApprovalDecision
{
    /// <summary>True = approved (proceed to board write); false = rejected (no write).</summary>
    public required bool IsApproved { get; init; }

    /// <summary>Reviewer identity from the decision card, if provided.</summary>
    public string? DecidedBy { get; init; }

    /// <summary>Optional reviewer note, appended to the board comment for traceability.</summary>
    public string? Note { get; init; }
}

/// <summary>A reference to a work item created or upserted on the board.</summary>
public record CreatedWorkItemRef
{
    /// <summary>Azure DevOps work item id.</summary>
    public required int WorkItemId { get; init; }

    /// <summary>Work item type — <c>Epic</c> / <c>Task</c> / <c>Bug</c>.</summary>
    public required string WorkItemType { get; init; }

    /// <summary>Browser/API url of the work item.</summary>
    public required string Url { get; init; }

    /// <summary>False = newly created; true = upserted (an existing item was refreshed).</summary>
    public bool WasUpdated { get; init; }
}
