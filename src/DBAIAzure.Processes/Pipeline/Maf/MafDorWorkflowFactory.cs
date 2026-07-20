// Builds the Intelligent DoR Validation Workflow as a MAF Workflow (spec-021). This increment wires the
// pass path (hydrate → review → transition → audit); the fail branch currently terminates at audit and is
// re-pointed to the conversational-resolution path in the HITL increment.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models.DorWorkflow;
using DBAIAzure.Processes.Executors.Dor;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Pipeline.Maf;

/// <summary>
/// Assembles the DoR workflow graph from its executors, branching on the review verdict with predicate edges
/// (the GA API has no <c>AddSwitch</c>). The work-tracker adapter passed in is the active one (resolved per run
/// by the caller), so no Web type leaks into this Processes-layer factory.
/// </summary>
public static class MafDorWorkflowFactory
{
    /// <summary>Builds a runnable DoR workflow whose start executor accepts the seed <see cref="DorRunState"/>.</summary>
    public static Workflow Build(
        IDorReviewService reviewService,
        IWorkTrackerAdapter activeAdapter,
        IDorDocumentSource documentSource,
        IDorConfigResolver configResolver,
        IMessageDelivery messageDelivery,
        IDorWorkflowInstanceStore instanceStore)
    {
        var hydrate = new HydrateExecutor(activeAdapter, documentSource, configResolver, instanceStore).BindExecutor();
        var review = new DorReviewExecutor(reviewService, configResolver, instanceStore).BindExecutor();
        var pass = new PassTransitionExecutor(activeAdapter, messageDelivery, configResolver, instanceStore).BindExecutor();
        var audit = new AuditExecutor(instanceStore).BindExecutor();

        return new WorkflowBuilder(hydrate)
            .AddEdge(hydrate, review)
            .AddEdge(review, pass, (DorRunState state) => state.ReviewPassed)     // pass path → transition
            .AddEdge(review, audit, (DorRunState state) => !state.ReviewPassed)   // fail path → terminal (HITL increment re-points this)
            .AddEdge(pass, audit)
            .WithOutputFrom(audit)
            .Build(validateOrphans: true);
    }
}
