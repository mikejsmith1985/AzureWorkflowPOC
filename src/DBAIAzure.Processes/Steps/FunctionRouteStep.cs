// Implements the Function-type workflow node that uses structured LLM output to select
// an output port, enforcing Article VII — no free-text string matching.
#pragma warning disable SKEXP0080

using System.Text;
using System.Text.Json;
using DBAIAzure.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// KernelProcessStep that routes a workflow run to a specific output port by asking the
/// LLM to choose from the node's configured port labels. Uses structured JSON schema binding
/// (Article VII) instead of free-text matching — deserialization failure or an unrecognised
/// port label causes an immediate NodeFailed event so the pipeline never silently misroutes.
/// </summary>
public sealed class FunctionRouteStep : KernelProcessStep
{
    private static readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Output port labels configured on this node by WorkflowRuntimeBuilder before the step
    /// executes. The LLM must select exactly one label from this list; any other value is
    /// treated as an unrecoverable routing error.
    /// </summary>
    public IReadOnlyList<string> KnownPortLabels { get; set; } = [];

    /// <summary>
    /// Inspects the step's input payload and asks the LLM — using a strict JSON response
    /// schema — which of the node's output ports should receive the workflow run next.
    /// Emits an event whose Id equals the chosen port label so the process framework can
    /// route the run to the correct downstream step without any string-matching heuristics.
    /// </summary>
    [KernelFunction]
    public async Task RouteAsync(
        KernelProcessStepContext ctx,
        WorkflowStepData stepData,
        Kernel kernel)
    {
        var chatService = kernel.GetRequiredService<IChatCompletionService>();

        var portList = string.Join(", ", KnownPortLabels);
        var prompt = $"""
            You are a workflow router. Given the following context, decide which output port to use.
            Context: {stepData.InputPayload}
            Available output ports: {portList}
            Respond ONLY with valid JSON matching this schema: {RouteDecisionSchema.JsonSchema}
            """;

        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage(prompt);

        // Collect the full response before attempting deserialization so that a partial
        // token stream never produces a spurious JSON parse error.
        var responseBuffer = new StringBuilder();
        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(chatHistory, kernel: kernel))
        {
            responseBuffer.Append(chunk.Content ?? string.Empty);
        }

        var rawResponse = responseBuffer.ToString().Trim();

        // Strip markdown code fences that some model configurations emit even when
        // instructed not to — the schema instruction takes precedence once the fence is removed.
        var jsonBody = rawResponse.StartsWith("```")
            ? string.Join('\n', rawResponse.Split('\n').Skip(1).TakeWhile(line => !line.StartsWith("```")))
            : rawResponse;

        RouteDecision? decision = null;
        try
        {
            decision = JsonSerializer.Deserialize<RouteDecision>(jsonBody, _jsonOptions);
        }
        catch (JsonException)
        {
            // Deserialization failure is unrecoverable — the model returned something the
            // schema cannot satisfy. Emit NodeFailed so the pipeline surfaces this clearly.
        }

        // Guard: a null decision or a label not in the configured port set is a routing
        // error that must not be silently ignored (Article X — proof required).
        var isValidPort = decision is not null
            && KnownPortLabels.Contains(decision.SelectedPortLabel, StringComparer.Ordinal);

        if (!isValidPort)
        {
            await ctx.EmitEventAsync(new()
            {
                Id   = WorkflowNodeEvents.NodeFailed,
                Data = stepData
            });
            return;
        }

        // Emit the matched port label as the event Id so the process framework routes
        // the run to whichever downstream step is wired to that label.
        await ctx.EmitEventAsync(new()
        {
            Id   = decision!.SelectedPortLabel,
            Data = stepData
        });
    }
}
