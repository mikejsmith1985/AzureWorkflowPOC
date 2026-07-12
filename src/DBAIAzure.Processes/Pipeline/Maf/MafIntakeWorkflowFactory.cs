// Builds the intake pipeline as a MAF Workflow (spec-019 T019) — the GA replacement for the SK
// IntakePipelineBuilder. Stub for now: the parity test (T014) is written first and drives this to green.
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Pipeline.Maf;

/// <summary>
/// Assembles the ticket-intake pipeline as a MAF <see cref="Workflow"/>: the executors
/// (<see cref="MafExecutorIds.Intake"/> → <see cref="MafExecutorIds.Validation"/> → ready path
/// <see cref="MafExecutorIds.Estimation"/> → <see cref="MafExecutorIds.Action"/>; not-ready path
/// <see cref="MafExecutorIds.GapAnalysis"/> → <see cref="MafExecutorIds.IntakeHitl"/>) wired with
/// conditional edges for the ready/not-ready branch. All model access flows through the provided
/// <see cref="IChatClient"/> (no provider-specific type in the graph). Replaces
/// <c>IntakePipelineBuilder</c> (SK <c>ProcessBuilder</c>).
/// </summary>
public static class MafIntakeWorkflowFactory
{
    /// <summary>
    /// Builds the intake workflow. <paramref name="chatClient"/> is the provider-neutral model client
    /// every LLM executor uses; <paramref name="services"/> supplies any additional per-executor
    /// dependencies (work-tracker adapter, repositories) resolved at build time.
    /// </summary>
    /// <returns>A runnable <see cref="Workflow"/> whose start executor accepts the initial ticket.</returns>
    public static Workflow Build(IChatClient chatClient, IServiceProvider? services = null)
    {
        // Implemented in US1 T017 (executors) + T019 (this graph). The parity test (T014) is authored
        // first and asserts the Intake→Validation→{Estimation→Action | GapAnalysis→Hitl} sequence.
        throw new NotImplementedException(
            "MafIntakeWorkflowFactory.Build is pending US1 (spec-019 T017/T019). Parity test T014 defines the target.");
    }
}
