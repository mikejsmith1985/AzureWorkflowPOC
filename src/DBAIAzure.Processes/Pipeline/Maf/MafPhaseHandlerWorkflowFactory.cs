// Builds the phase-handler pipeline as a MAF Workflow (spec-019 T020) — the GA replacement for the SK
// PhaseHandlerPipelineBuilder. Stub for now: the parity test (T015) is written first and drives it green.
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
        // Implemented in US1 T017 (executors) + T020 (this graph) and US2 (RequestPort approval gate).
        // Parity test T015 is authored first and asserts the read→validate→approval→(resume)→create shape.
        throw new NotImplementedException(
            "MafPhaseHandlerWorkflowFactory.Build is pending US1 (spec-019 T017/T020). Parity test T015 defines the target.");
    }
}
