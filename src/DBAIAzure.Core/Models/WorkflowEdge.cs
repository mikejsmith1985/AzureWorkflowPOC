// Defines the edge model used to represent directed connections between workflow node ports.


namespace DBAIAzure.Core.Models;

/// <summary>
/// Represents a directed connection between an output port on one workflow node
/// and an input port on another. Edges carry a human-readable label (e.g. "Approved",
/// "Result") that the visual builder renders on the connecting arrow, making pipeline
/// routing legible without inspecting the underlying node configuration.
/// </summary>
/// <param name="Id">
/// Stable 8-character hex identifier that uniquely addresses this edge within its
/// parent workflow definition. Generated once at creation time and never changed.
/// </param>
/// <param name="SourceNodeId">Identifier of the node whose output port originates this edge.</param>
/// <param name="SourcePortId">Identifier of the specific output port on the source node.</param>
/// <param name="TargetNodeId">Identifier of the node whose input port receives this edge.</param>
/// <param name="TargetPortId">Identifier of the specific input port on the target node.</param>
/// <param name="Label">
/// Plain-language text shown on the connecting arrow in the visual builder,
/// describing the semantic meaning of this connection (e.g. "Approved", "Result").
/// </param>
public sealed record WorkflowEdge(
    string Id,
    string SourceNodeId,
    string SourcePortId,
    string TargetNodeId,
    string TargetPortId,
    string Label)
{
    /// <summary>
    /// Creates a new <see cref="WorkflowEdge"/> with a freshly generated stable ID,
    /// wiring the given source port to the given target port. Use this factory
    /// whenever the visual builder or a deserialization path needs to mint a brand-new
    /// edge rather than reconstitute an existing one from persisted state.
    /// </summary>
    /// <param name="sourceNodeId">Identifier of the node that owns the originating port.</param>
    /// <param name="sourcePortId">Identifier of the output port on the source node.</param>
    /// <param name="targetNodeId">Identifier of the node that owns the receiving port.</param>
    /// <param name="targetPortId">Identifier of the input port on the target node.</param>
    /// <param name="label">Human-readable label rendered on the connecting arrow.</param>
    /// <returns>A fully initialised <see cref="WorkflowEdge"/> with a unique 8-character hex ID.</returns>
    public static WorkflowEdge CreateNew(
        string sourceNodeId,
        string sourcePortId,
        string targetNodeId,
        string targetPortId,
        string label)
    {
        var stableId = GenerateShortId();
        return new WorkflowEdge(stableId, sourceNodeId, sourcePortId, targetNodeId, targetPortId, label);
    }

    /// <summary>
    /// Generates a stable 8-character lowercase hex identifier by taking the first
    /// 4 bytes of a new <see cref="Guid"/>. The probability of collision within a
    /// single workflow (typically dozens of edges) is negligible, and the short form
    /// keeps serialized definitions readable.
    /// </summary>
    private static string GenerateShortId()
    {
        // Use the first 4 bytes (8 hex chars) of a new Guid as a compact, URL-safe ID.
        const int shortIdByteCount = 4;
        return Guid.NewGuid().ToString("N")[..(shortIdByteCount * 2)];
    }
}
