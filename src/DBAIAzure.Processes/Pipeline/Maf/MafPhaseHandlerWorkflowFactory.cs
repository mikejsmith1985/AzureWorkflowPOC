// Builds the phase-handler pipeline as a MAF Workflow — the GA replacement for the SK
// PhaseHandlerPipelineBuilder: read → validate → approval RequestPort → create-on-approval (spec-019 T020/T027).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Ai;
using DBAIAzure.Processes.Executors;
using DBAIAzure.Processes.Pipeline;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Pipeline.Maf;

/// <summary>
/// Assembles the phase-handler pipeline as a MAF <see cref="Workflow"/>:
/// <see cref="MafExecutorIds.ReadArtifacts"/> → <see cref="MafExecutorIds.PhaseValidation"/> →
/// <see cref="MafExecutorIds.ApprovalHitl"/> (a <see cref="RequestPort"/> approval gate) →
/// <see cref="MafExecutorIds.CreateWorkItem"/> on the reviewer-decided state.
/// </summary>
public static class MafPhaseHandlerWorkflowFactory
{
    /// <summary>
    /// Builds the phase-handler workflow from explicitly-supplied dependencies: <paramref name="chatClient"/>
    /// is the provider-neutral model client the validation executor uses; <paramref name="artifactReader"/>
    /// reads the phase artifacts; <paramref name="progressSink"/> receives per-step progress (nullable);
    /// <paramref name="bindingKeyMinter"/> mints the cost binding key (nullable); and
    /// <paramref name="writerDeps"/> carries the board-write dependencies the create executor needs.
    /// </summary>
    /// <returns>A runnable <see cref="Workflow"/> whose start executor accepts the phase signal.</returns>
    public static Workflow Build(
        IChatClient chatClient,
        IArtifactReader artifactReader,
        IPhaseProgressSink? progressSink,
        IBindingKeyMinter? bindingKeyMinter,
        PhaseWorkItemWriterDeps writerDeps)
    {
        var structuredService = new ChatClientStructuredCompletionService(chatClient);

        var readArtifacts = new ReadArtifactsExecutor(artifactReader, progressSink).BindExecutor();
        var validation = new PhaseValidationExecutor(structuredService, bindingKeyMinter, progressSink).BindExecutor();
        var createWorkItem = new CreateWorkItemExecutor(writerDeps, progressSink).BindExecutor();

        // The reviewer approval gate: a request carrying the validated state, resolved by the host with the
        // reviewer-decided state (its Decision populated). The run suspends here (RequestInfoEvent) until the
        // host responds; the decided state then flows to the create executor, which writes the board only on
        // an approved decision (FR-006). Response type matches the request type so the full state — not just
        // the decision — reaches the create step (parity with the intake HITL port).
        var approval = RequestPort.Create<PhaseHandlerState, PhaseHandlerState>(MafExecutorIds.ApprovalHitl)
            .BindAsExecutor(allowWrappedRequests: false);

        return new WorkflowBuilder(readArtifacts)
            .AddEdge(readArtifacts, validation)
            .AddEdge(validation, approval)
            .AddEdge(approval, createWorkItem)          // resume: the decided state drives the board write
            // readArtifacts/validation yield a failed terminal state on their error paths; createWorkItem
            // yields the terminal Completed/Rejected/Unsupported/Failed state on the resume path.
            .WithOutputFrom(readArtifacts, validation, createWorkItem)
            .Build(validateOrphans: true);
    }
}
