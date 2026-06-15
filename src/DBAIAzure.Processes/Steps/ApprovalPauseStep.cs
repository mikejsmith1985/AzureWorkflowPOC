// SK step: emits the external AwaitApproval event that suspends the process for human review.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Suspends the process until the reviewer decides. Emits the public
/// <see cref="PhaseHandlerEvents.AwaitApproval"/> event; the orchestrator detects the pause and
/// resumes the process with the decision. No board write can happen until after this gate, which
/// is the heart of FR-006 (no autonomous action).
/// </summary>
public sealed class ApprovalPauseStep : KernelProcessStep
{
    /// <summary>Transitions the state to AwaitingApproval and emits the external pause event.</summary>
    [KernelFunction]
    public async Task PauseForApprovalAsync(KernelProcessStepContext ctx, PhaseHandlerState state, Kernel kernel)
    {
        var awaiting = state with { Status = PhaseRunStatus.AwaitingApproval };
        kernel.Services.GetService<IPhaseProgressSink>()?.Report(awaiting);
        await ctx.EmitEventAsync(new() { Id = PhaseHandlerEvents.AwaitApproval, Data = awaiting });
    }
}
