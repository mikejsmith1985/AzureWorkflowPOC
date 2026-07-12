// Builds the phase-handler pipeline as a MAF Workflow (spec-019 T020) — the GA replacement for the SK
// PhaseHandlerPipelineBuilder: read → validate → approval RequestPort. Create-on-approval resume is US2.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Processes.Ai;
using DBAIAzure.Processes.Executors;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Pipeline.Maf;

/// <summary>
/// Assembles the phase-handler pipeline as a MAF <see cref="Workflow"/>:
/// <see cref="MafExecutorIds.ReadArtifacts"/> → <see cref="MafExecutorIds.PhaseValidation"/> →
/// <see cref="MafExecutorIds.ApprovalHitl"/> (a <see cref="RequestPort"/> approval gate) →
/// <see cref="MafExecutorIds.CreateWorkItem"/> on the approved decision. Replaces
/// <c>PhaseHandlerPipelineBuilder</c> (SK <c>ProcessBuilder</c> + proxy step).
/// </summary>
public static class MafPhaseHandlerWorkflowFactory
{
    /// <summary>
    /// Builds the phase-handler workflow. <paramref name="chatClient"/> is the provider-neutral model
    /// client used by the validation executor; <paramref name="services"/> supplies the work-tracker
    /// adapter, cost ledger, and repositories the create executor needs.
    /// </summary>
    /// <returns>A runnable <see cref="Workflow"/> whose start executor accepts the phase signal.</returns>
    public static Workflow Build(IChatClient chatClient, IServiceProvider? services = null)
    {
        var artifactReader = services?.GetService(typeof(IArtifactReader)) as IArtifactReader
            ?? throw new InvalidOperationException(
                "An IArtifactReader must be supplied (via services) to build the phase-handler workflow.");
        var progressSink = services?.GetService(typeof(IPhaseProgressSink)) as IPhaseProgressSink;
        var bindingKeyMinter = services?.GetService(typeof(IBindingKeyMinter)) as IBindingKeyMinter;
        var structuredService = new ChatClientStructuredCompletionService(chatClient);

        var readArtifacts = new ReadArtifactsExecutor(artifactReader, progressSink).BindExecutor();
        var validation = new PhaseValidationExecutor(structuredService, bindingKeyMinter, progressSink).BindExecutor();

        // The reviewer approval gate: a request carrying the validated state, resolved by the reviewer's
        // ApprovalDecision. The run suspends here (RequestInfoEvent). Create-on-approval + resume is US2.
        var approval = RequestPort.Create<PhaseHandlerState, ApprovalDecision>(MafExecutorIds.ApprovalHitl)
            .BindAsExecutor(allowWrappedRequests: false);

        return new WorkflowBuilder(readArtifacts)
            .AddEdge(readArtifacts, validation)
            .AddEdge(validation, approval)
            .WithOutputFrom(readArtifacts, validation) // both yield a failed terminal state on their error paths
            .Build(validateOrphans: true);
    }
}
