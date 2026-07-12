// MAF executor for a visual FunctionRoute node (spec-019 T018) — the GA replacement for the SK
// FunctionRouteStep. Uses structured LLM output to choose an output port, then routes the run along that
// port's edge via a directed message to the target node (parity with the SK port-label event routing).
using System.Text.Json;
using DBAIAzure.Core.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Executors;

/// <summary>
/// Routes a workflow run to one of the node's output ports by asking the model — using a strict JSON
/// schema (Article VII, no free-text matching) — which port to take, then directs the run payload to that
/// port's target node. An unusable decision falls back to the realized default port when one exists;
/// otherwise the run yields a failed terminal payload. Mirrors the retired <c>FunctionRouteStep</c>.
/// </summary>
[SendsMessage(typeof(WorkflowStepData))]
[YieldsOutput(typeof(WorkflowStepData))]
public sealed class FunctionRouteExecutor : Executor<WorkflowStepData>
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IChatClient _chatClient;
    private readonly IReadOnlyList<string> _knownPortLabels;
    private readonly IReadOnlyDictionary<string, string> _targetNodeByPortLabel;
    private readonly string? _defaultPortLabel;

    /// <summary>
    /// Creates the route executor for one canvas node. <paramref name="targetNodeByPortLabel"/> maps each
    /// output port label to the id of the node its edge leads to (used for the directed send).
    /// </summary>
    public FunctionRouteExecutor(
        string nodeId,
        IChatClient chatClient,
        IReadOnlyList<string> knownPortLabels,
        IReadOnlyDictionary<string, string> targetNodeByPortLabel,
        string? defaultPortLabel = null)
        : base(nodeId)
    {
        _chatClient = chatClient;
        _knownPortLabels = knownPortLabels;
        _targetNodeByPortLabel = targetNodeByPortLabel;
        _defaultPortLabel = defaultPortLabel;
    }

    /// <inheritdoc />
    public override async ValueTask HandleAsync(WorkflowStepData stepData, IWorkflowContext context, CancellationToken cancellationToken)
    {
        var prompt = $"""
            You are a workflow router. Given the following context, decide which output port to use.
            Context: {stepData.InputPayload}
            Available output ports: {string.Join(", ", _knownPortLabels)}
            Respond ONLY with valid JSON matching this schema: {RouteDecisionSchema.JsonSchema}
            """;

        var responseText = await ExecutorLlm.CompleteAsync(_chatClient, prompt, cancellationToken);
        var json = ExecutorLlm.StripCodeFences(responseText);

        RouteDecision? decision = null;
        try { decision = JsonSerializer.Deserialize<RouteDecision>(json, JsonOptions); }
        catch (JsonException) { /* an unbindable response falls through to the default/failed path */ }

        var isValidPort = decision is not null
            && _knownPortLabels.Contains(decision.SelectedPortLabel, StringComparer.Ordinal);

        var chosenLabel = isValidPort ? decision!.SelectedPortLabel : _defaultPortLabel;

        // Route to the chosen (or default) port's target node via a directed message. A missing target
        // means the run has nowhere valid to go — yield a failed terminal payload rather than misroute.
        if (chosenLabel is not null && _targetNodeByPortLabel.TryGetValue(chosenLabel, out var targetNodeId))
        {
            await context.SendMessageAsync(stepData, targetNodeId, cancellationToken);
            return;
        }

        await context.YieldOutputAsync(
            stepData with { OutputPayload = "Route node could not resolve a valid output port." }, cancellationToken);
    }
}
