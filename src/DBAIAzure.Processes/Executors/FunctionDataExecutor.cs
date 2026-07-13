// MAF executor for a visual FunctionData node (spec-019 T018) — the GA replacement for the SK
// FunctionDataStep. Same connector-readiness check and operation description (parity — FR-015).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Processes.Pipeline;
using DBAIAzure.Processes.Steps;
using Microsoft.Agents.AI.Workflows;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// Reads from or writes to the node's realized <see cref="DataNodeConfig"/> data source: it confirms the
/// bound connector is configured (secrets resolved server-side via <see cref="IConnectorConfigRepository"/>)
/// and surfaces the operation result, or passes the payload through when un-realized, then forwards it — or
/// yields it when terminal. Reuses <see cref="FunctionDataStep.DescribeOperation"/> for identical output.
/// </summary>
[SendsMessage(typeof(WorkflowStepData))]
[YieldsOutput(typeof(WorkflowStepData))]
public sealed class FunctionDataExecutor : Executor<WorkflowStepData>
{
    private readonly NodeRuntimeConfig _config;
    private readonly bool _isTerminal;
    private readonly IConnectorConfigRepository? _connectorRepository;

    /// <summary>Creates the data executor for one canvas node.</summary>
    public FunctionDataExecutor(
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
        var data = NodeConfigSerializer.ReadConfig<DataNodeConfig>(_config.FunctionConfig);

        string? output;
        if (data is null)
        {
            // Un-realized node: pass the payload through unchanged so the workflow still runs.
            output = stepData.InputPayload;
        }
        else
        {
            var isConnectorReady = await IsConnectorConfiguredAsync(data.Connector);
            output = FunctionDataStep.DescribeOperation(data, stepData.InputPayload, isConnectorReady);
        }

        await NodeCompletion.EmitAsync(context, stepData with { OutputPayload = output }, _isTerminal, cancellationToken);
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
}
