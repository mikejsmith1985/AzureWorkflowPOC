// Builds the phase-handler pipeline as a MAF Workflow (spec-019 T020) — the GA replacement for the SK
// PhaseHandlerPipelineBuilder: read → validate → approval RequestPort. Create-on-approval resume is US2.
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
/// <see cref="MafExecutorIds.CreateWorkItem"/> on the reviewer-decided state. Replaces
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
        var createWorkItem = new CreateWorkItemExecutor(ResolveWriterDeps(services), progressSink).BindExecutor();

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

    /// <summary>
    /// Resolves the board-write dependencies for the create executor from the supplied services. Only the
    /// work-tracker adapter is needed to actually write; the cost/telemetry services are optional and
    /// degrade gracefully when absent (best-effort — FR-011).
    /// </summary>
    private static PhaseWorkItemWriterDeps ResolveWriterDeps(IServiceProvider? services)
    {
        TService? Resolve<TService>() where TService : class => services?.GetService(typeof(TService)) as TService;

        return new PhaseWorkItemWriterDeps(
            Tracker:         Resolve<IWorkTrackerAdapter>()!,
            Repository:      Resolve<IPhaseRunRepository>(),
            BindingMap:      Resolve<IBindingWorkItemMap>(),
            Ledger:          Resolve<ICostLedger>(),
            TelemetrySource: Resolve<IRunTelemetrySource>(),
            Projection:      Resolve<ICostProjection>(),
            WriteBack:       Resolve<ITelemetryWriteBack>());
    }
}
