// Evaluates a human reply against the outstanding DoR gaps and sets the routing flags the graph branches on
// (spec-021 US2): resolved → ticket update; unresolved but within the iteration budget → follow-up outreach;
// otherwise → manual exit.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Pipeline.Maf;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>
/// Interprets the human reply carried on the resumed payload, increments the iteration count (an answered
/// exchange), and computes the next route: <see cref="DorRunState.JustResolved"/> for resolution,
/// <see cref="DorRunState.ContinueConversation"/> for a focused follow-up within the iteration budget, or neither
/// (manual exit) once the budget is exhausted.
/// </summary>
[SendsMessage(typeof(DorRunState))]
public sealed class ReplyEvalExecutor : Executor<DorRunState>
{
    private readonly IDorConversationService _conversationService;
    private readonly IDorConfigResolver _configResolver;
    private readonly IDorWorkflowInstanceStore _instanceStore;

    public ReplyEvalExecutor(
        IDorConversationService conversationService, IDorConfigResolver configResolver, IDorWorkflowInstanceStore instanceStore)
        : base(MafExecutorIds.DorReplyEval)
    {
        _conversationService = conversationService;
        _configResolver = configResolver;
        _instanceStore = instanceStore;
    }

    /// <inheritdoc/>
    public override async ValueTask HandleAsync(DorRunState state, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var config = await _configResolver.ResolveActiveAsync(cancellationToken);

        // The escalation tier has its own iteration counter and budget (FR-017).
        var isEscalation = state.SlaTier == SlaTier.Escalation;
        var iteration = (isEscalation ? state.EscalationIterations : state.PrimaryIterations) + 1;
        var maxIterations = isEscalation ? config.Comms.Escalation.MaxIterations : config.Comms.Primary.MaxIterations;

        var evaluation = await _conversationService.EvaluateReplyAsync(
            state.OutstandingGaps, state.HumanReply ?? string.Empty, iteration, config.Ai, cancellationToken);

        var canContinue = !evaluation.Resolved && iteration < maxIterations;

        var next = state with
        {
            PrimaryIterations = isEscalation ? state.PrimaryIterations : iteration,
            EscalationIterations = isEscalation ? iteration : state.EscalationIterations,
            HumanReply = null,
            JustResolved = evaluation.Resolved,
            ContinueConversation = canContinue,
            OutstandingGaps = evaluation.Resolved ? Array.Empty<string>() : evaluation.RemainingGaps,
            ResolvedFieldUpdates = evaluation.Resolved ? evaluation.FieldUpdates : state.ResolvedFieldUpdates,
            PendingOutreachMessage = canContinue ? evaluation.ReplyMessage : null,
            // Resolved → update; still resolvable → loop; otherwise → manual exit.
            State = evaluation.Resolved ? DorState.Updating : (canContinue ? DorState.Reviewing : DorState.ManualExit),
            FailureReason = (!evaluation.Resolved && !canContinue)
                ? "Conversation iteration limit reached without resolution."
                : state.FailureReason,
        };

        await _instanceStore.UpdateAsync(DorInstanceMapper.ToInstance(next), cancellationToken);
        await context.SendMessageAsync(next, cancellationToken);
    }
}
