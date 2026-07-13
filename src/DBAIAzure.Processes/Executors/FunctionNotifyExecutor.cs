// MAF executor for a visual FunctionNotify node (spec-019 T018) — the GA replacement for the SK
// FunctionNotifyStep. Same realized-config rendering and un-realized pass-through (parity — FR-015).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Processes.Pipeline;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// Sends a notification described by the node's realized <see cref="NotifyNodeConfig"/> (or forwards the
/// input unchanged when the node is un-realized), then forwards the run payload to the next node — or
/// yields it as the workflow output when this node is terminal. Mirrors the retired
/// <c>FunctionNotifyStep</c>; secrets are resolved server-side via <see cref="IConnectorConfigRepository"/>.
/// </summary>
[SendsMessage(typeof(WorkflowStepData))]
[YieldsOutput(typeof(WorkflowStepData))]
public sealed class FunctionNotifyExecutor : Executor<WorkflowStepData>
{
    private readonly NodeRuntimeConfig _config;
    private readonly bool _isTerminal;
    private readonly IConnectorConfigRepository? _connectorRepository;

    /// <summary>Creates the notify executor for one canvas node, with its realized config and terminality.</summary>
    public FunctionNotifyExecutor(
        string nodeId, NodeRuntimeConfig config, bool isTerminal, IConnectorConfigRepository? connectorRepository = null)
        : base(nodeId)
    {
        _config = config;
        _isTerminal = isTerminal;
        _connectorRepository = connectorRepository;
    }

    /// <inheritdoc />
    public override async ValueTask HandleAsync(WorkflowStepData stepData, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var notify = NodeConfigSerializer.ReadConfig<NotifyNodeConfig>(_config.FunctionConfig);

        string outputSummary;
        if (notify is null)
        {
            // Un-realized node: keep the prior pass-through behaviour so the workflow still runs.
            outputSummary = stepData.InputPayload ?? string.Empty;
        }
        else
        {
            var isConnectorReady = await IsConnectorConfiguredAsync(notify.Connector);
            var message = RenderTemplate(notify.MessageTemplate, stepData.InputPayload);
            outputSummary = isConnectorReady
                ? $"Sent via {notify.Connector} to {notify.RecipientMap}: {message}"
                : $"Could not send via {notify.Connector} — connector is not set up.";
        }

        await NodeCompletion.EmitAsync(context, stepData with { OutputPayload = outputSummary }, _isTerminal, cancellationToken);
    }

    /// <summary>Confirms the bound connector exists and is configured, resolving secrets server-side only.</summary>
    private async Task<bool> IsConnectorConfiguredAsync(ConnectorType connector)
    {
        if (_connectorRepository is null)
        {
            return false;
        }
        var config = await _connectorRepository.GetAsync(connector);
        return config is { IsConfigured: true };
    }

    /// <summary>Substitutes the upstream payload into the message template's <c>{input}</c> placeholder.</summary>
    private static string RenderTemplate(string template, string? input) =>
        string.IsNullOrWhiteSpace(template)
            ? input ?? string.Empty
            : template.Replace("{input}", input ?? string.Empty, StringComparison.OrdinalIgnoreCase);
}
