// Projects the in-flight DoR payload (DorRunState) onto the persisted instance row (DorWorkflowInstance) so
// executors can save state after each transition (spec-021 FR-031). StartedAt is set once at creation by the
// orchestrator and left untouched by updates.
using DBAIAzure.Core.Models.DorWorkflow;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>Maps the run payload to its persisted instance row for a state save.</summary>
internal static class DorInstanceMapper
{
    /// <summary>Builds an instance snapshot from the current run state, stamping the update/complete times.</summary>
    public static DorWorkflowInstance ToInstance(DorRunState state) => new()
    {
        RunId = state.RunId,
        TicketKey = state.TicketKey,
        State = state.State,
        OutstandingGaps = state.OutstandingGaps,
        PrimaryIterations = state.PrimaryIterations,
        EscalationIterations = state.EscalationIterations,
        SlaClockStartedAt = state.SlaClockStartedAt,
        SlaDeadlineAt = state.SlaDeadlineAt,
        SlaTier = state.SlaTier,
        ActiveChannelId = state.ActiveChannelId,
        ThreadRef = state.ThreadRef,
        LastSeenReplyRef = state.LastSeenReplyRef,
        IsDryRun = state.IsDryRun,
        Outcome = state.Outcome,
        FailureReason = state.FailureReason,
        UpdatedAt = DateTimeOffset.UtcNow,
        CompletedAt = state.State == DorState.Done ? DateTimeOffset.UtcNow : null,
    };
}
