// Kernel process step that suspends a workflow run at a human approval gate and waits
// for the orchestrator to resume it with an approval or rejection decision.
#pragma warning disable SKEXP0080

using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Processes.Pipeline;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Pauses a workflow run at a human approval gate. Reuses the
/// <see cref="IExternalKernelProcessMessageChannel"/> HITL pattern from
/// <see cref="HitlPauseStep"/>. The orchestrator resumes via SubmitApproval(runId, approved).
///
/// When <see cref="WaitForApprovalAsync"/> executes it emits an
/// <c>AwaitHumanApproval</c> public event so the running process transitions to Paused.
/// When the node has been realized, the configured <see cref="ApprovalNodeConfig.PromptShown"/> is
/// carried into the pause payload so the reviewer sees the question the author wrote (FR-15.5).
/// The orchestrator later re-enters the process by emitting <c>HumanApprovalReceived</c> carrying a
/// <see cref="WorkflowStepData"/> with <see cref="WorkflowStepData.IsApproved"/> set to the decision.
///
/// Note: The <see cref="ExternalChannel"/> proxy wiring is done in WorkflowRuntimeBuilder;
/// this step only emits the suspend event and does not inspect the resume payload directly.
/// </summary>
public sealed class HumanApprovalStep : KernelProcessStep<NodeRuntimeConfig>
{
    private NodeRuntimeConfig _config = new();

    /// <summary>
    /// External message channel injected by WorkflowRuntimeBuilder so the process can
    /// surface the AwaitHumanApproval event to the host runner without tight coupling.
    /// May be null when running in unit-test scenarios that do not wire the proxy.
    /// </summary>
    public IExternalKernelProcessMessageChannel? ExternalChannel { get; set; }

    /// <summary>Captures the per-node configuration injected at build time.</summary>
    public override ValueTask ActivateAsync(KernelProcessStepState<NodeRuntimeConfig> state)
    {
        _config = state.State ?? new NodeRuntimeConfig();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Emits the <c>AwaitHumanApproval</c> external event, suspending this workflow run
    /// until the orchestrator injects a <c>HumanApprovalReceived</c> event to resume it.
    /// The step carries the current <paramref name="stepData"/> in the event payload so
    /// the host runner can correlate the paused run by <see cref="WorkflowStepData.RunId"/>.
    /// </summary>
    /// <param name="ctx">SK process step context used to emit the suspend event.</param>
    /// <param name="stepData">The workflow payload at the point of suspension; forwarded with the
    /// configured approval prompt so the resuming event can merge the decision into it.</param>
    /// <param name="kernel">The active kernel instance (reserved for future diagnostics).</param>
    [KernelFunction]
    public async Task WaitForApprovalAsync(
        KernelProcessStepContext ctx,
        WorkflowStepData stepData,
        Kernel kernel)
    {
        // Surface the realized approval prompt (if any) so the reviewer sees the author's question.
        var approval = NodeConfigSerializer.ReadConfig<ApprovalNodeConfig>(_config.FunctionConfig);
        var pausePayload = approval is null || string.IsNullOrWhiteSpace(approval.PromptShown)
            ? stepData
            : stepData with { OutputPayload = approval.PromptShown };

        // Emit the public event that signals the process host to pause execution.
        // The process framework routes this through the proxy step and onto ExternalChannel,
        // allowing the orchestrator to detect WasPaused and queue the run for review.
        await ctx.EmitEventAsync(new()
        {
            Id = Events.AwaitHumanApproval,
            Data = pausePayload,
        });
    }
}
