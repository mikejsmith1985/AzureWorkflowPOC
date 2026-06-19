// Notification step — sends a Teams, email, or other alert based on the node's FunctionConfig.
#pragma warning disable SKEXP0080

using DBAIAzure.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Sends a notification (Teams, email, etc.) based on FunctionConfig. For POC, logs the notification.
/// In production this step reads the node's <c>FunctionConfig</c> to resolve the target channel
/// and message template, then delegates to the appropriate connector. Emits
/// <see cref="WorkflowNodeEvents.NodeCompleted"/> once the notification has been dispatched.
/// </summary>
public sealed class FunctionNotifyStep : KernelProcessStep
{
    /// <summary>
    /// Dispatches the notification described in <paramref name="stepData"/> and signals
    /// pipeline completion for this node. For POC the dispatch is a log statement;
    /// a production implementation would resolve an <c>INotificationConnector</c> from
    /// the kernel's DI container and await its send method.
    /// </summary>
    /// <param name="ctx">Process-step context used to emit lifecycle events.</param>
    /// <param name="stepData">Immutable data envelope carrying the run ID, node ID, and input payload.</param>
    /// <param name="kernel">Semantic Kernel instance — provides access to DI services including connectors.</param>
    [KernelFunction]
    public async Task NotifyAsync(
        KernelProcessStepContext ctx,
        WorkflowStepData stepData,
        Kernel kernel)
    {
        var logger = kernel.LoggerFactory.CreateLogger<FunctionNotifyStep>();

        // Log the notification so the POC produces observable output; replace this
        // with a real connector call (Teams webhook, SendGrid, etc.) in production.
        logger.LogInformation(
            "Notification sent for run {RunId}, node {NodeId}: {Payload}",
            stepData.RunId,
            stepData.NodeId,
            stepData.InputPayload);

        var completedData = stepData with { OutputPayload = stepData.InputPayload };

        await ctx.EmitEventAsync(new() { Id = WorkflowNodeEvents.NodeCompleted, Data = completedData });
    }
}
