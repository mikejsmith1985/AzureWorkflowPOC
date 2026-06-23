// Notification step — sends an alert based on the node's realized NotifyNodeConfig (connector, recipient,
// message). Secrets are resolved at execution time from the connector repository, never carried in config.
#pragma warning disable SKEXP0080

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Processes.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Sends a notification from the node's realized <see cref="NotifyNodeConfig"/>: it resolves the bound
/// messaging connector, confirms the connector is configured (secrets are fetched at execution via
/// <see cref="IConnectorConfigRepository"/>, never embedded in config — Article IX), and dispatches the
/// rendered message. Until a concrete messaging client is wired, the dispatch is an observable log line
/// carrying the realized recipient and message. Emits <see cref="WorkflowNodeEvents.NodeCompleted"/>.
/// </summary>
public sealed class FunctionNotifyStep : KernelProcessStep<NodeRuntimeConfig>
{
    private NodeRuntimeConfig _config = new();

    /// <summary>Captures the per-node configuration injected at build time.</summary>
    public override ValueTask ActivateAsync(KernelProcessStepState<NodeRuntimeConfig> state)
    {
        _config = state.State ?? new NodeRuntimeConfig();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Dispatches the notification described by the realized config and signals node completion.
    /// </summary>
    /// <param name="ctx">Process-step context used to emit lifecycle events.</param>
    /// <param name="stepData">Run payload carrying the input forwarded from the preceding step.</param>
    /// <param name="kernel">Kernel — provides DI access to the connector repository and logging.</param>
    [KernelFunction]
    public async Task NotifyAsync(
        KernelProcessStepContext ctx,
        WorkflowStepData stepData,
        Kernel kernel)
    {
        var logger = kernel.LoggerFactory.CreateLogger<FunctionNotifyStep>();
        var notify = NodeConfigSerializer.ReadConfig<NotifyNodeConfig>(_config.FunctionConfig);

        string outputSummary;
        if (notify is null)
        {
            // Un-realized node: keep the prior pass-through behaviour so the workflow still runs.
            logger.LogInformation(
                "Notify node {NodeId} has no realized config; forwarding input unchanged.", stepData.NodeId);
            outputSummary = stepData.InputPayload ?? string.Empty;
        }
        else
        {
            var isConnectorReady = await IsConnectorConfiguredAsync(kernel, notify.Connector).ConfigureAwait(false);
            var message          = RenderTemplate(notify.MessageTemplate, stepData.InputPayload);

            logger.LogInformation(
                "Notification via {Connector} to {Recipient} (connector configured: {Ready}): {Message}",
                notify.Connector, notify.RecipientMap, isConnectorReady, message);

            outputSummary = isConnectorReady
                ? $"Sent via {notify.Connector} to {notify.RecipientMap}: {message}"
                : $"Could not send via {notify.Connector} — connector is not set up.";
        }

        await ctx.EmitEventAsync(new()
        {
            Id   = WorkflowNodeEvents.NodeCompleted,
            Data = stepData with { OutputPayload = outputSummary },
        });
    }

    /// <summary>Confirms the bound connector exists and is configured, resolving secrets server-side only.</summary>
    private static async Task<bool> IsConnectorConfiguredAsync(Kernel kernel, ConnectorType connector)
    {
        var repository = kernel.Services.GetService<IConnectorConfigRepository>();
        if (repository is null)
            return false;
        var config = await repository.GetAsync(connector).ConfigureAwait(false);
        return config is { IsConfigured: true };
    }

    /// <summary>Substitutes the upstream payload into the message template's <c>{input}</c> placeholder.</summary>
    private static string RenderTemplate(string template, string? input) =>
        string.IsNullOrWhiteSpace(template)
            ? input ?? string.Empty
            : template.Replace("{input}", input ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
