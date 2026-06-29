// Step-level audit record written by IWorkflowObserver on every node entry, exit, and LLM call.

namespace DBAIAzure.Core.Models;

/// <summary>
/// An immutable audit record for a single observable point during a workflow execution.
/// The full set of events for a run forms the Execution History timeline in the UI.
/// AI steps additionally populate the LLM-cost fields so token usage is queryable.
/// </summary>
public sealed record WorkflowExecutionEvent(
    /// <summary>Surrogate key — assigned by the observer on write.</summary>
    Guid EventId,

    /// <summary>The run that produced this event.</summary>
    string RunId,

    /// <summary>Canvas node that raised the event; null for run-level events.</summary>
    string? NodeId,

    /// <summary>Human-readable label of the node at the time of the event.</summary>
    string? NodeLabel,

    /// <summary>Category of the observable point.</summary>
    WorkflowEventType EventType,

    /// <summary>UTC instant the event occurred.</summary>
    DateTimeOffset OccurredAt,

    /// <summary>Wall-clock duration of the step in milliseconds; null for instantaneous events.</summary>
    long? DurationMs,

    /// <summary>Short outcome label (e.g. "Approved", "Rejected", error message excerpt).</summary>
    string? Outcome,

    // ── LLM-cost visibility fields (FR-21.2) — null for non-AI events ──────────

    /// <summary>Model identifier as returned by the LLM provider (e.g. "claude-sonnet-4-6").</summary>
    string? LlmModelName,

    /// <summary>Number of tokens in the prompt sent to the LLM.</summary>
    int? LlmInputTokens,

    /// <summary>Number of tokens in the completion returned by the LLM.</summary>
    int? LlmOutputTokens,

    /// <summary>Prompt tokens served from cache (reuse). Null for non-AI / no-cache events.</summary>
    int? LlmCacheReadTokens = null,

    /// <summary>Prompt tokens written to cache. Null for non-AI / no-cache events.</summary>
    int? LlmCacheCreationTokens = null
);
