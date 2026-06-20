// Defines the port model used to represent connection points on workflow nodes.

#pragma warning disable SKEXP0080

namespace DBAIAzure.Core.Models;

/// <summary>
/// Indicates whether a port accepts incoming data or emits outgoing data,
/// driving how the visual builder renders and validates node connections.
/// </summary>
public enum PortDirection
{
    /// <summary>The port receives data flowing into the node.</summary>
    Input,

    /// <summary>The port emits data flowing out of the node.</summary>
    Output,
}

/// <summary>
/// Represents a single named connection point on a workflow node.
/// Ports are the typed anchors that the visual builder uses to draw and
/// validate edges between steps in a pipeline.
/// </summary>
/// <param name="Id">Stable, unique identifier for this port within its parent node.</param>
/// <param name="Label">Human-readable label shown in the visual builder UI.</param>
/// <param name="Direction">Whether this port accepts input or produces output.</param>
public sealed record WorkflowPort(string Id, string Label, PortDirection Direction);
