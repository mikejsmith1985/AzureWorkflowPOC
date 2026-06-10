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

        // Entry: new ticket arrives
        builder
            .OnInputEvent(Events.TicketReceived)
            .SendEventTo(new ProcessFunctionTargetBuilder(intake));

        // HITL resume: external event from runner after human answers
        // This routes HumanResponded (injected by the runner) directly to ValidationStep.
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

        // GapAnalysisStep → HitlPauseStep (emits public event, process suspends)
        gapAnalysis
            .OnEvent(Events.QuestionsReady)
            .SendEventTo(new ProcessFunctionTargetBuilder(hitlPause));

        // EstimationStep → ActionStep (terminal)
        estimation
            .OnEvent(Events.EstimationComplete)
            .SendEventTo(new ProcessFunctionTargetBuilder(action));

        return builder.Build();
    }
}
