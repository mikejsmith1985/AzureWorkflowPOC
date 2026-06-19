// Generic agentic step — drives any agentic canvas node by streaming an LLM response
// for the goal prompt injected via WorkflowRuntimeBuilder per-node configuration.
#pragma warning disable SKEXP0080

using DBAIAzure.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// Generic agentic step — reads GoalPrompt from step data parameter (injected by
/// WorkflowRuntimeBuilder per-node via ProcessFunctionTargetBuilder). One step type
/// handles all agentic node types; the goal differentiates them.
/// </summary>
public sealed class AgenticNodeStep : KernelProcessStep
{
    /// <summary>
    /// Executes the agentic node by streaming an LLM response for the configured goal
    /// prompt, then emits <see cref="WorkflowNodeEvents.NodeCompleted"/> with the
    /// accumulated output so the downstream router can forward it to the next step.
    /// </summary>
    /// <param name="ctx">
    /// Semantic Kernel step context used to emit process events back into the pipeline.
    /// </param>
    /// <param name="stepData">
    /// Immutable payload carrying the run identifier, node identifier, optional goal
    /// prompt, and any input forwarded from the preceding step.
    /// </param>
    /// <param name="kernel">
    /// The configured <see cref="Kernel"/> instance, used to resolve the chat completion
    /// service wired up during pipeline initialisation.
    /// </param>
    [KernelFunction]
    public async Task ExecuteAsync(
        KernelProcessStepContext ctx,
        WorkflowStepData stepData,
        Kernel kernel)
    {
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        var history = new ChatHistory();
        history.AddSystemMessage(stepData.GoalPrompt ?? "Complete the assigned task.");
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
}
