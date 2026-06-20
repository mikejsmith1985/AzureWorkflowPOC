// Defines the structured LLM output contract for FunctionRouteStep's port-selection decision.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Typed result of FunctionRouteStep's structured LLM call. SelectedPortLabel must exactly match
/// one of the node's output port labels; the step emits NodeFailed if no match is found.
/// </summary>
public sealed record RouteDecision(string SelectedPortLabel);

/// <summary>
/// Provides the JSON schema used to request structured output from IChatCompletionService
/// when performing a route decision.
/// </summary>
public static class RouteDecisionSchema
{
    /// <summary>
    /// Passed as the response_format JSON schema to IChatCompletionService to enforce structured
    /// output (Article VII — no free-text parsing).
    /// </summary>
    public const string JsonSchema =
        """{"type":"object","required":["SelectedPortLabel"],"properties":{"SelectedPortLabel":{"type":"string","description":"Exact label of the output port to route to"}}}""";
}
