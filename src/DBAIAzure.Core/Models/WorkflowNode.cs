// Represents a single node on the visual workflow canvas — the fundamental building
// block from which users compose agentic pipelines.

namespace DBAIAzure.Core.Models;

/// <summary>
/// An immutable snapshot of a single workflow node as it exists on the canvas.
/// Nodes are the fundamental unit of composition: each node represents one discrete
/// step (trigger, agent action, function, or control flow) that the runtime will
/// execute in sequence or in parallel according to the edges that connect them.
/// </summary>
public sealed record WorkflowNode
{
    /// <summary>
    /// Stable short identifier — 8 hex characters derived from a <see cref="Guid"/>,
    /// unique within the containing workflow. Used as the join key in edge definitions
    /// and in serialized workflow JSON.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Broad category of this node that determines which runtime handler executes it
    /// and which configuration fields are applicable.
    /// </summary>
    public required WorkflowNodeType NodeType { get; init; }

    /// <summary>
    /// Plain-language name displayed on the canvas tile. Limited to 50 characters so
    /// it fits comfortably in the node chip without truncation at standard zoom levels.
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Agentic and Trigger nodes: the user's plain-language description of what this step
    /// should accomplish (for AgenticReason nodes) or what event starts this workflow
    /// (for Trigger nodes). Sent verbatim to the LLM as the task goal. Null for function
    /// and control-flow node types that carry no goal description.
    /// </summary>
    public string? GoalPrompt { get; init; }

    /// <summary>
    /// Human-readable label describing the data this step expects to receive from its
    /// upstream connection. Surfaced in the canvas tooltip and the connector configuration
    /// modal to guide the user when wiring edges.
    /// </summary>
    public string? InputLabel { get; init; }

    /// <summary>
    /// Human-readable description of what this step produces and passes to downstream
    /// nodes. Surfaced in the connector configuration modal so users understand what they
    /// are connecting when they draw an edge from this node.
    /// </summary>
    public string? OutputLabel { get; init; }

    /// <summary>
    /// JSON-serialized blob that holds the per-type configuration specific to this node.
    /// For Trigger nodes: <c>{"initialDataDescription":""}</c> where <c>initialDataDescription</c>
    /// maps to the "What information is available at the start?" field.
    /// For function nodes: HTTP method/URL, Azure Function name, etc.
    /// Null for node types that carry no additional configuration.
    /// </summary>
    public string? FunctionConfig { get; init; }

    /// <summary>
    /// Horizontal position of the node's top-left corner on the canvas, measured in
    /// logical canvas units. The builder grid-snaps this value so connected edges remain
    /// aligned after drag operations.
    /// </summary>
    public required double PositionX { get; init; }

    /// <summary>
    /// Vertical position of the node's top-left corner on the canvas, measured in
    /// logical canvas units. Grid-snapped in the same way as <see cref="PositionX"/>.
    /// </summary>
    public required double PositionY { get; init; }

    /// <summary>
    /// Indicates whether all fields required to execute this node have been supplied by
    /// the user. The builder uses this flag to render the configuration-incomplete
    /// warning badge and to gate the "Run workflow" action until every node is valid.
    /// </summary>
    public required bool IsConfigured { get; init; }

    /// <summary>
    /// Named connection points on the left (incoming) side of the node. Each port
    /// represents one logical data or control-flow channel that upstream nodes can
    /// attach an edge to. Trigger nodes always have an empty list here.
    /// </summary>
    public required IReadOnlyList<WorkflowPort> InputPorts { get; init; }

    /// <summary>
    /// Named connection points on the right (outgoing) side of the node. Each port
    /// represents one logical data or control-flow channel that this node can forward
    /// to downstream nodes.
    /// </summary>
    public required IReadOnlyList<WorkflowPort> OutputPorts { get; init; }

    /// <summary>
    /// Creates a new <see cref="WorkflowNode"/> with a freshly generated 8-character
    /// hex identifier and sensible defaults per node type.
    /// Trigger nodes are created with their fixed port topology (zero input ports, one
    /// "Begin" output port) and Trigger-specific field defaults so callers do not need
    /// to know the convention. All other node types get empty port lists for the caller
    /// to populate with the correct topology.
    /// </summary>
    /// <param name="nodeType">The category of node being created.</param>
    /// <param name="label">Display name shown on the canvas tile (max 50 chars).</param>
    /// <returns>
    /// A <see cref="WorkflowNode"/> with a unique ID. For Trigger nodes, ports and domain
    /// fields are fully initialised; for other types, port lists are empty and callers
    /// must attach the correct ports before storing the record.
    /// </returns>
    public static WorkflowNode CreateNew(WorkflowNodeType nodeType, string label)
    {
        if (nodeType == WorkflowNodeType.Trigger)
        {
            // Trigger nodes have a fixed topology: zero input ports, one "Begin" output port.
            // The factory fully initialises them so callers never need to know this convention.
            var beginPort = new WorkflowPort("begin", "Begin", PortDirection.Output);
            return new WorkflowNode
            {
                Id             = Guid.NewGuid().ToString("N")[..8],
                NodeType       = nodeType,
                Label          = label,
                GoalPrompt     = string.Empty,
                InputLabel     = "Trigger",
                OutputLabel    = "Begin",
                FunctionConfig = "{\"initialDataDescription\":\"\"}",
                IsConfigured   = false,
                PositionX      = 0,
                PositionY      = 0,
                InputPorts     = Array.Empty<WorkflowPort>(),
                OutputPorts    = new[] { beginPort }.ToList().AsReadOnly(),
            };
        }

        return new WorkflowNode
        {
            Id           = Guid.NewGuid().ToString("N")[..8],
            NodeType     = nodeType,
            Label        = label,
            IsConfigured = false,
            PositionX    = 0,
            PositionY    = 0,
            InputPorts   = Array.Empty<WorkflowPort>(),
            OutputPorts  = Array.Empty<WorkflowPort>(),
        };
    }
}
