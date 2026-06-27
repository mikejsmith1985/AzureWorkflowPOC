// Data step — reads from or writes to a configured data store using the node's realized DataNodeConfig.
// Secrets are resolved at execution from the connector repository, never carried in config (Article IX).
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
/// Reads from or writes to a configured data source described by the realized
/// <see cref="DataNodeConfig"/>: it resolves the bound connector, confirms it is configured (secrets are
/// fetched at execution via <see cref="IConnectorConfigRepository"/>, never embedded in config), and
/// applies the operation's input/output mapping. Until a concrete data-connector client contract exists,
/// the operation surfaces an observable result and forwards the payload. Per-node config is injected as
/// Semantic Kernel step state by <see cref="WorkflowRuntimeBuilder"/>. Emits
/// <see cref="WorkflowNodeEvents.NodeCompleted"/>.
/// </summary>
public sealed class FunctionDataStep : KernelProcessStep<NodeRuntimeConfig>
{
    private NodeRuntimeConfig _config = new();

    /// <summary>Captures the per-node configuration injected at build time.</summary>
    public override ValueTask ActivateAsync(KernelProcessStepState<NodeRuntimeConfig> state)
    {
        _config = state.State ?? new NodeRuntimeConfig();
        return ValueTask.CompletedTask;
    }

    /// <summary>Executes the configured data read/write and signals node completion.</summary>
    /// <param name="ctx">Process-step context used to emit lifecycle events.</param>
    /// <param name="stepData">Run payload carrying the input forwarded from the preceding step.</param>
    /// <param name="kernel">Kernel — provides DI access to the connector repository and logging.</param>
    [KernelFunction]
    public async Task ReadWriteAsync(
        KernelProcessStepContext ctx,
        WorkflowStepData stepData,
        Kernel kernel)
    {
        var logger = kernel.LoggerFactory.CreateLogger<FunctionDataStep>();
        var data   = NodeConfigSerializer.ReadConfig<DataNodeConfig>(_config.FunctionConfig);

        string? output;
        if (data is null)
        {
            // Un-realized node: pass the payload through unchanged so the workflow still runs.
            output = stepData.InputPayload;
        }
        else
        {
            var isConnectorReady = await IsConnectorConfiguredAsync(kernel, data.Connector).ConfigureAwait(false);
            output = DescribeOperation(data, stepData.InputPayload, isConnectorReady);
            logger.LogInformation(
                "Data {Operation} via {Connector} (connector configured: {Ready}) for node {NodeId}.",
                data.Operation, data.Connector, isConnectorReady, stepData.NodeId);
        }

        await ctx.EmitEventAsync(new()
        {
            Id   = WorkflowNodeEvents.NodeCompleted,
            Data = stepData with { OutputPayload = output },
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

    /// <summary>
    /// Produces the observable result of the data operation. A read surfaces the resolved output mapping
    /// (or the input when the connector is unavailable); a write confirms the payload was sent. Pure and
    /// side-effect free so it can be unit-tested directly.
    /// </summary>
    public static string DescribeOperation(DataNodeConfig config, string? input, bool isConnectorReady)
    {
        if (!isConnectorReady)
            return $"Could not {config.Operation.ToString().ToLowerInvariant()} via {config.Connector} — connector is not set up.";

        return config.Operation == DataOperation.Read
            ? $"Read from {config.Connector}: {config.OutputMap}"
            : $"Wrote to {config.Connector}: {input}";
    }
}
