using DBAIAzure.Processes.Steps;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes;

/// <summary>
/// Wires the intake pipeline using SK Process Framework's ProcessBuilder.
/// Maps 1:1 to LangGraph's StateGraph + add_conditional_edges.
///
/// HITL design: HitlPauseStep emits a public AwaitHuman event (exits the process boundary).
/// The runner subscribes, collects input, then sends HumanResponded back to the PROCESS
/// via runningProcess.SendMessageAsync(). The process routes that external event directly
/// to ValidationStep for re-validation — same semantic as LangGraph interrupt/resume.
/// </summary>
public static class IntakePipelineBuilder
{
    public static KernelProcess Build()
    {
        var builder = new ProcessBuilder("IntakePipeline");

        var intake = builder.AddStepFromType<IntakeStep>();
        var validation = builder.AddStepFromType<ValidationStep>();
        var gapAnalysis = builder.AddStepFromType<GapAnalysisStep>();
        var hitlPause = builder.AddStepFromType<HitlPauseStep>();
        var estimation = builder.AddStepFromType<EstimationStep>();
        var action = builder.AddStepFromType<ActionStep>();

        // Proxy step: routes AwaitHuman events out of the process boundary to
        // IExternalKernelProcessMessageChannel — the runner's HitlExternalChannel.
        // Interview talking point: this is the SK Process Framework equivalent of
        // LangGraph's interrupt() — one line to cross the process boundary.
        var hitlProxy = builder.AddProxyStep("hitl-proxy", [Events.AwaitHuman], []);

        // Entry: new ticket arrives
        builder
            .OnInputEvent(Events.TicketReceived)
            .SendEventTo(new ProcessFunctionTargetBuilder(intake));

        // HITL resume: external event injected by the runner after the human answers.
        // The runner starts a fresh process with this event, routing directly to
        // ValidationStep — skipping intake so the ticket is not re-normalized.
        builder
            .OnInputEvent(Events.HumanResponded)
            .SendEventTo(new ProcessFunctionTargetBuilder(validation));

        // IntakeStep → ValidationStep
        intake
            .OnEvent(Events.IntakeComplete)
            .SendEventTo(new ProcessFunctionTargetBuilder(validation));

        // ValidationStep: ready path → estimate
        validation
            .OnEvent(Events.ReadyPath)
            .SendEventTo(new ProcessFunctionTargetBuilder(estimation));

        // ValidationStep: not-ready path → clarify
        validation
            .OnEvent(Events.NotReadyPath)
            .SendEventTo(new ProcessFunctionTargetBuilder(gapAnalysis));

        // GapAnalysisStep → HitlPauseStep
        gapAnalysis
            .OnEvent(Events.QuestionsReady)
            .SendEventTo(new ProcessFunctionTargetBuilder(hitlPause));

        // HitlPauseStep → proxy → IExternalKernelProcessMessageChannel
        // After this edge fires, the process has no further internal events and terminates.
        // RunToEndAsync unblocks; the runner reads the channel and collects PO input.
        hitlPause
            .OnEvent(Events.AwaitHuman)
            .EmitExternalEvent(hitlProxy, Events.AwaitHuman);

        // EstimationStep → ActionStep (terminal)
        estimation
            .OnEvent(Events.EstimationComplete)
            .SendEventTo(new ProcessFunctionTargetBuilder(action));

        return builder.Build();
    }
}
