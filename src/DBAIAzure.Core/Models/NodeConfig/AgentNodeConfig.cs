// Realized configuration for an AgenticReason node: everything the runtime needs to run the agent.

namespace DBAIAzure.Core.Models.NodeConfig;

/// <summary>
/// The executable configuration for an AI/agentic step, produced by realization and consumed by the
/// runtime's agentic step. Holds the operating instruction, the model to use, the structured output
/// shape, and any tool/connector bindings the agent may call.
/// </summary>
public sealed record AgentNodeConfig
{
    /// <summary>The operating instruction sent to the model (what this step should accomplish).</summary>
    public required string Instruction { get; init; }

    /// <summary>The model the agent runs on (defaults to the workspace LLM connector's model).</summary>
    public required string ModelRef { get; init; }

    /// <summary>
    /// The structured fields the agent produces. Must be non-empty when this node feeds a Route or
    /// Transform node, so downstream routing/mapping is deterministic (FR-14.2).
    /// </summary>
    public IReadOnlyList<OutputField> OutputShape { get; init; } = Array.Empty<OutputField>();

    /// <summary>
    /// Connectors the agent is allowed to call as tools. Each must be a configured connector, or the
    /// node is reported as blocked (FR-14.3).
    /// </summary>
    public IReadOnlyList<ConnectorType> ToolBindings { get; init; } = Array.Empty<ConnectorType>();
}
