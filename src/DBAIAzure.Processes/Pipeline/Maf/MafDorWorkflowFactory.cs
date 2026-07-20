// Builds the Intelligent DoR Validation Workflow as a MAF Workflow (spec-021): the pass path (hydrate → review →
// transition → audit) and the human-in-the-loop conversational-resolution path (fail → outreach → HITL gate →
// reply-eval → update/follow-up/manual-exit).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Executors.Dor;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Pipeline.Maf;

/// <summary>
/// Assembles the DoR workflow graph from its executors, branching with predicate edges (the GA API has no
/// <c>AddSwitch</c>). The human gate is a native <see cref="RequestPort"/> carrying the paused
/// <see cref="DorRunState"/>. The work-tracker adapter passed in is the active one (resolved per run by the
/// caller), so no Web type leaks into this Processes-layer factory.
/// </summary>
public static class MafDorWorkflowFactory
{
    /// <summary>Builds a runnable DoR workflow whose start executor accepts the seed <see cref="DorRunState"/>.</summary>
    public static Workflow Build(
        IDorReviewService reviewService,
        IDorConversationService conversationService,
        IWorkTrackerAdapter activeAdapter,
        IDorDocumentSource documentSource,
        IDorConfigResolver configResolver,
        IMessageDelivery messageDelivery,
        IDorWorkflowInstanceStore instanceStore)
    {
        var hydrate = new HydrateExecutor(activeAdapter, documentSource, configResolver, instanceStore).BindExecutor();
        var review = new DorReviewExecutor(reviewService, configResolver, instanceStore).BindExecutor();
        var pass = new PassTransitionExecutor(activeAdapter, messageDelivery, configResolver, instanceStore).BindExecutor();
        var outreach = new GapOutreachExecutor(messageDelivery, configResolver, instanceStore).BindExecutor();
        var replyEval = new ReplyEvalExecutor(conversationService, configResolver, instanceStore).BindExecutor();
        var update = new TicketUpdateExecutor(activeAdapter, configResolver, instanceStore).BindExecutor();
        var audit = new AuditExecutor(instanceStore).BindExecutor();

        // The HITL gate: a request carrying the paused run state, resolved by the host with the same state
        // carrying the human reply. The run suspends here (RequestInfoEvent) until the orchestrator responds.
        var hitl = RequestPort.Create<DorRunState, DorRunState>(MafExecutorIds.DorHitl).BindAsExecutor(allowWrappedRequests: false);

        return new WorkflowBuilder(hydrate)
            .AddEdge(hydrate, review)
            .AddEdge(review, pass, (DorRunState state) => state.ReviewPassed)           // ready → transition
            .AddEdge(review, outreach, (DorRunState state) => !state.ReviewPassed)       // not ready → conversation
            .AddEdge(outreach, hitl)                                                     // post gaps → suspend for a human
            .AddEdge(hitl, replyEval)                                                    // resume with the reply
            .AddEdge(replyEval, update, (DorRunState state) => state.JustResolved)        // resolved → write + transition
            .AddEdge(replyEval, outreach, (DorRunState state) => state.ContinueConversation) // partial → focused follow-up
            .AddEdge(replyEval, audit, (DorRunState state) => !state.JustResolved && !state.ContinueConversation) // exhausted → manual exit
            .AddEdge(pass, audit)
            .AddEdge(update, audit)
            .WithOutputFrom(audit)
            .Build(validateOrphans: true);
    }
}
