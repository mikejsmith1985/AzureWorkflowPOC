// EF Core entity for step-level audit events written by IWorkflowObserver (FR-21).

namespace DBAIAzure.Storage.Entities;

/// <summary>
/// One row per observable event (step entry, step exit, LLM call, run pause/resume).
/// The full set for a run forms the Execution History timeline.
/// LLM-cost fields are null for non-AI events.
/// Maps to the <c>WorkflowExecutionEvents</c> table.
/// </summary>
public sealed class WorkflowExecutionEventEntity
{
    /// <summary>Surrogate primary key — assigned by the observer on write.</summary>
    public Guid EventId { get; set; }

    /// <summary>Foreign key to <see cref="WorkflowRunEntity.RunId"/>.</summary>
    public string RunId { get; set; } = string.Empty;

    /// <summary>Canvas node that raised the event; null for run-level events.</summary>
    public string? NodeId { get; set; }

    /// <summary>Human-readable label of the node at the time of the event.</summary>
    public string? NodeLabel { get; set; }

    /// <summary>Event category stored as its integer ordinal.</summary>
    public int EventType { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Wall-clock step duration in milliseconds; null for instantaneous events.</summary>
    public long? DurationMs { get; set; }

    /// <summary>Short outcome label or error excerpt.</summary>
    public string? Outcome { get; set; }

    // ── LLM-cost fields (FR-21.2) ─────────────────────────────────────────────

    public string? LlmModelName { get; set; }
    public int? LlmInputTokens { get; set; }
    public int? LlmOutputTokens { get; set; }
}
