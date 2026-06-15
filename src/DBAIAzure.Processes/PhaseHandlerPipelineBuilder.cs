// Wires the phase-handler SK process graph: read → validate → approval pause → (on decision) create.
using DBAIAzure.Processes.Steps;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes;

/// <summary>
/// Builds the phase-handler process using SK Process Framework's ProcessBuilder, mirroring the
/// ticket pipeline's <c>IntakePipelineBuilder</c>. The approval pause crosses the process boundary
/// via a proxy step (HITL), and the orchestrator restarts the process with the decision to resume.
/// </summary>
public static class PhaseHandlerPipelineBuilder
{
    public static KernelProcess Build()
    {
        var builder = new ProcessBuilder("PhaseHandlerPipeline");

        var readArtifacts = builder.AddStepFromType<ReadArtifactsStep>();
        var validation    = builder.AddStepFromType<PhaseValidationStep>();
        var approvalPause = builder.AddStepFromType<ApprovalPauseStep>();
        var createWorkItem = builder.AddStepFromType<CreateWorkItemStep>();

        // Proxy step: routes AwaitApproval out of the process to the ApprovalExternalChannel,
        // the SK Process Framework equivalent of LangGraph's interrupt().
        var approvalProxy = builder.AddProxyStep("approval_proxy", [PhaseHandlerEvents.AwaitApproval], []);

        // Entry: a validated phase signal starts the read step.
        builder
            .OnInputEvent(PhaseHandlerEvents.PhaseSignalReceived)
            .SendEventTo(new ProcessFunctionTargetBuilder(readArtifacts));

        // Resume: the reviewer's decision (delivered by the orchestrator) routes straight to the
        // create step — skipping read/validate, which already ran before the pause.
        builder
            .OnInputEvent(PhaseHandlerEvents.ApprovalDecided)
            .SendEventTo(new ProcessFunctionTargetBuilder(createWorkItem));

        // ReadArtifactsStep → PhaseValidationStep (only the success path continues).
        readArtifacts
            .OnEvent(PhaseHandlerEvents.ArtifactsRead)
            .SendEventTo(new ProcessFunctionTargetBuilder(validation));

        // PhaseValidationStep → ApprovalPauseStep.
        validation
            .OnEvent(PhaseHandlerEvents.Validated)
            .SendEventTo(new ProcessFunctionTargetBuilder(approvalPause));

        // ApprovalPauseStep → proxy → ApprovalExternalChannel. The process then terminates;
        // RunToEndAsync unblocks and the orchestrator reads the channel.
        approvalPause
            .OnEvent(PhaseHandlerEvents.AwaitApproval)
            .EmitExternalEvent(approvalProxy, PhaseHandlerEvents.AwaitApproval);

        return builder.Build();
    }
}
