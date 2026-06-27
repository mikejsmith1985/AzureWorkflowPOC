// Generic agentic step — drives any agentic canvas node by streaming an LLM response. When the node
// has been realized it executes the accepted AgentNodeConfig (instruction + model); otherwise it falls
// back to the node's plain-language GoalPrompt so un-realized workflows still run.
#pragma warning disable SKEXP0080

using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Processes.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Generic agentic step. Its per-node configuration is injected by <see cref="WorkflowRuntimeBuilder"/>
/// as Semantic Kernel step state. Once a node is realized, the step uses the accepted
/// <see cref="AgentNodeConfig.Instruction"/> as the operating instruction; before realization it uses
/// the node's <see cref="WorkflowNode.GoalPrompt"/>. Emits <see cref="WorkflowNodeEvents.NodeCompleted"/>
/// with the accumulated output so the downstream router can forward it.
/// </summary>
public sealed class AgenticNodeStep : KernelProcessStep<NodeRuntimeConfig>
{
    // Default system instruction when neither realized config nor a goal prompt is available.
    private const string DefaultInstruction = "Complete the assigned task.";

    // Captured from step state on activation so the KernelFunction can read this node's configuration.
    private NodeRuntimeConfig _config = new();

    /// <summary>Captures the per-node configuration injected at build time.</summary>
    public override ValueTask ActivateAsync(KernelProcessStepState<NodeRuntimeConfig> state)
    {
        _config = state.State ?? new NodeRuntimeConfig();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Executes the agentic node by streaming an LLM response for the resolved instruction, then emits
    /// <see cref="WorkflowNodeEvents.NodeCompleted"/> carrying the accumulated output.
    /// </summary>
    /// <param name="ctx">SK step context used to emit process events back into the pipeline.</param>
    /// <param name="stepData">Run payload carrying the input forwarded from the preceding step.</param>
    /// <param name="kernel">Configured kernel; resolves the chat-completion service.</param>
    [KernelFunction]
    public async Task ExecuteAsync(
        KernelProcessStepContext ctx,
        WorkflowStepData stepData,
        Kernel kernel)
    {
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        history.AddSystemMessage(ResolveInstruction(stepData));
        history.AddUserMessage(stepData.InputPayload ?? "Begin.");

        var fullText = new StringBuilder();

        try
        {
            await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(history, kernel: kernel))
            {
                fullText.Append(chunk.Content ?? string.Empty);
            }
        }
        catch (Exception streamException)
        {
            throw new LlmUnavailableException(
                "The AI assistant is currently unavailable. Please try again shortly.",
                streamException);
        }

        await ctx.EmitEventAsync(new()
        {
            Id   = WorkflowNodeEvents.NodeCompleted,
            Data = stepData with { OutputPayload = fullText.ToString() }
        });
    }

    /// <summary>
    /// Resolves the operating instruction in priority order: the realized agent instruction, then the
    /// node's plain-language goal (from state or the incoming payload), then a generic default.
    /// </summary>
    private string ResolveInstruction(WorkflowStepData stepData)
    {
        var realized = NodeConfigSerializer.ReadConfig<AgentNodeConfig>(_config.FunctionConfig);
        if (realized is not null && !string.IsNullOrWhiteSpace(realized.Instruction))
            return realized.Instruction;

        if (!string.IsNullOrWhiteSpace(_config.GoalPrompt))
            return _config.GoalPrompt!;

        return stepData.GoalPrompt ?? DefaultInstruction;
    }
}
