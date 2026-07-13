// MAF executor for a visual AgenticReason node (spec-019 T018) — the GA replacement for the SK
// AgenticNodeStep. Same instruction-resolution order and LLM-unavailable behaviour (parity — FR-015).
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Processes.Pipeline;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// Drives an agentic canvas node by asking the model for a response to the resolved instruction (the
/// realized <see cref="AgentNodeConfig.Instruction"/>, else the node's goal, else a default), then forwards
/// the output — or yields it when the node is terminal. Mirrors the retired <c>AgenticNodeStep</c>.
/// </summary>
[SendsMessage(typeof(WorkflowStepData))]
[YieldsOutput(typeof(WorkflowStepData))]
public sealed class AgenticNodeExecutor : Executor<WorkflowStepData>
{
    private const string DefaultInstruction = "Complete the assigned task.";

    private readonly IChatClient _chatClient;
    private readonly NodeRuntimeConfig _config;
    private readonly bool _isTerminal;

    /// <summary>Creates the agentic node executor for one canvas node.</summary>
    public AgenticNodeExecutor(string nodeId, IChatClient chatClient, NodeRuntimeConfig config, bool isTerminal)
        : base(nodeId)
    {
        _chatClient = chatClient;
        _config = config;
        _isTerminal = isTerminal;
    }

    /// <inheritdoc />
    public override async ValueTask HandleAsync(WorkflowStepData stepData, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var messages = new[]
        {
            new ChatMessage(Microsoft.Extensions.AI.ChatRole.System, ResolveInstruction(stepData)),
            new ChatMessage(Microsoft.Extensions.AI.ChatRole.User, stepData.InputPayload ?? "Begin."),
        };

        string output;
        try
        {
            var response = await _chatClient.GetResponseAsync(messages, options: null, cancellationToken);
            output = (response.Text ?? string.Empty).Trim();
        }
        catch (Exception exception)
        {
            throw new LlmUnavailableException(
                "The AI assistant is currently unavailable. Please try again shortly.", exception);
        }

        await NodeCompletion.EmitAsync(context, stepData with { OutputPayload = output }, _isTerminal, cancellationToken);
    }

    /// <summary>Realized agent instruction → node goal → incoming goal → default (parity with the SK step).</summary>
    private string ResolveInstruction(WorkflowStepData stepData)
    {
        var realized = NodeConfigSerializer.ReadConfig<AgentNodeConfig>(_config.FunctionConfig);
        if (realized is not null && !string.IsNullOrWhiteSpace(realized.Instruction))
        {
            return realized.Instruction;
        }
        if (!string.IsNullOrWhiteSpace(_config.GoalPrompt))
        {
            return _config.GoalPrompt!;
        }
        return stepData.GoalPrompt ?? DefaultInstruction;
    }
}
