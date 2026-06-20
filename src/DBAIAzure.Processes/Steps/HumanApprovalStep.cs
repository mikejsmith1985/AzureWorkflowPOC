// Kernel process step that suspends a workflow run at a human approval gate and waits
// for the orchestrator to resume it with an approval or rejection decision.
#pragma warning disable SKEXP0080

using DBAIAzure.Core.Models;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Pauses a workflow run at a human approval gate. Reuses the
/// <see cref="IExternalKernelProcessMessageChannel"/> HITL pattern from
/// <see cref="HitlPauseStep"/>. The orchestrator resumes via SubmitApproval(runId, approved).
///
/// When <see cref="WaitForApprovalAsync"/> executes it emits an
/// <c>AwaitHumanApproval</c> public event so the running process transitions to Paused.
/// The orchestrator later re-enters the process by emitting
/// <c>HumanApprovalReceived</c> carrying a <see cref="WorkflowStepData"/> with
/// <see cref="WorkflowStepData.IsApproved"/> set to the reviewer's decision.
///
/// Note: The <see cref="ExternalChannel"/> proxy wiring is done in WorkflowRuntimeBuilder;
/// this step only emits the suspend event and does not inspect the resume payload directly.
/// </summary>
public sealed class HumanApprovalStep : KernelProcessStep
{
    /// <summary>
    /// External message channel injected by WorkflowRuntimeBuilder so the process can
    /// surface the AwaitHumanApproval event to the host runner without tight coupling.
    /// May be null when running in unit-test scenarios that do not wire the proxy.
    /// </summary>
    public IExternalKernelProcessMessageChannel? ExternalChannel { get; set; }

    /// <summary>
    /// Emits the <c>AwaitHumanApproval</c> external event, suspending this workflow run
    /// until the orchestrator injects a <c>HumanApprovalReceived</c> event to resume it.
    /// The step carries the current <paramref name="stepData"/> in the event payload so
    /// the host runner can correlate the paused run by <see cref="WorkflowStepData.RunId"/>.
    /// </summary>
    /// <param name="ctx">SK process step context used to emit the suspend event.</param>
    /// <param name="stepData">The workflow payload at the point of suspension; forwarded
    /// unchanged so the resuming event can merge the approval decision into it.</param>
    /// <param name="kernel">The active kernel instance (reserved for future diagnostics).</param>
    [KernelFunction]
    public async Task WaitForApprovalAsync(
        KernelProcessStepContext ctx,
        WorkflowStepData stepData,
        Kernel kernel)
    {
        // Emit the public event that signals the process host to pause execution.
        // The process framework routes this through the proxy step and onto ExternalChannel,
        // allowing the orchestrator to detect WasPaused and queue the run for review.
        await ctx.EmitEventAsync(new()
        {
            Id = Events.AwaitHumanApproval,
            Data = stepData,
        });
    }
}
