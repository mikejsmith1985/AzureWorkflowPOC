// Implements the Function-type workflow node that uses structured LLM output to select
// an output port, enforcing Article VII — no free-text string matching.
#pragma warning disable SKEXP0080

using System.Text;
using System.Text.Json;
using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;
using DBAIAzure.Processes.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DBAIAzure.Processes.Steps;

/// <summary>
/// KernelProcessStep that routes a workflow run to a specific output port by asking the
/// LLM to choose from the node's configured port labels. Uses structured JSON schema binding
/// (Article VII) instead of free-text matching — deserialization failure or an unrecognised
/// port label causes an immediate NodeFailed event so the pipeline never silently misroutes.
/// The candidate port labels and any realized <see cref="RouteNodeConfig"/> branching rules are
/// injected per node as Semantic Kernel step state by <see cref="WorkflowRuntimeBuilder"/>.
/// </summary>
public sealed class FunctionRouteStep : KernelProcessStep<NodeRuntimeConfig>
{
    private static readonly JsonSerializerOptions _jsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private NodeRuntimeConfig _config = new();

    /// <summary>
    /// Output port labels the LLM must choose from. Populated from step state on activation; also
    /// settable directly so unit tests can drive the step without building a whole process.
    /// </summary>
    public IReadOnlyList<string> KnownPortLabels { get; set; } = [];

    /// <summary>Captures per-node state and seeds <see cref="KnownPortLabels"/> from the node's ports.</summary>
    public override ValueTask ActivateAsync(KernelProcessStepState<NodeRuntimeConfig> state)
    {
        _config = state.State ?? new NodeRuntimeConfig();
        if (KnownPortLabels.Count == 0 && _config.OutputPortLabels.Count > 0)
            KnownPortLabels = _config.OutputPortLabels;
        return ValueTask.CompletedTask;
    }

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
            Available output ports: {portList}{BranchingGuidance()}
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
        // error. When the node has a realized default path, take it rather than failing the run
        // (FR-15.3 — a branch always has a deterministic next step); otherwise emit NodeFailed.
        var isValidPort = decision is not null
            && KnownPortLabels.Contains(decision.SelectedPortLabel, StringComparer.Ordinal);

        if (!isValidPort)
        {
            var defaultLabel = ResolveDefaultPortLabel();
            if (defaultLabel is not null)
            {
                await ctx.EmitEventAsync(new() { Id = defaultLabel, Data = stepData });
                return;
            }

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

    /// <summary>Appends the realized branch conditions to the router prompt, when present, as guidance.</summary>
    private string BranchingGuidance()
    {
        var route = NodeConfigSerializer.ReadConfig<RouteNodeConfig>(_config.FunctionConfig);
        if (route is null || route.Conditions.Count == 0)
            return string.Empty;

        var rules = string.Join("; ", route.Conditions.Select(condition =>
            $"choose '{condition.OutputPortId}' when {condition.Expression}"));
        return $"\nBranching rules: {rules}.";
    }

    /// <summary>
    /// Resolves the label of the realized default port so routing stays deterministic when the LLM's
    /// choice is unusable. Returns null when the node has no realized default that maps to a known port.
    /// </summary>
    private string? ResolveDefaultPortLabel()
    {
        var route = NodeConfigSerializer.ReadConfig<RouteNodeConfig>(_config.FunctionConfig);
        if (route is null || string.IsNullOrWhiteSpace(route.DefaultPortId))
            return null;

        // RouteNodeConfig references ports by id; the runtime routes by label. The default id often
        // matches a known label directly (canvas ids and labels frequently coincide for branch ports).
        return KnownPortLabels.Contains(route.DefaultPortId, StringComparer.Ordinal)
            ? route.DefaultPortId
            : null;
    }
}
