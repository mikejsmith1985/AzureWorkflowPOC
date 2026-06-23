// Output contract for the whole-workflow chat generation skill (FR-23).

namespace DBAIAzure.Core.Models;

/// <summary>
/// Structured result returned by <c>WorkflowDesignSkillService.GenerateWorkflowAsync</c>.
/// When <see cref="ClarifyingQuestion"/> is non-null the LLM decided the description was
/// ambiguous; callers must present the question to the user before re-invoking.
/// </summary>
public sealed record WorkflowGenerationResult(
    /// <summary>Generated nodes, empty when a clarifying question is required.</summary>
    IReadOnlyList<GeneratedNode> Nodes,

    /// <summary>Generated edges connecting the nodes in the intended execution order.</summary>
    IReadOnlyList<GeneratedEdge> Edges,

    /// <summary>
    /// When non-null, the LLM needs one answer before it can produce a complete graph.
    /// Nodes and Edges are empty in this case.
    /// </summary>
    string? ClarifyingQuestion
);

/// <summary>A single node proposed by the generation LLM.</summary>
public sealed record GeneratedNode(
    /// <summary>Temporary id used to wire edges; not the final canvas node id.</summary>
    string Id,

    /// <summary>Target node type on the canvas (must match <c>WorkflowNodeType</c> names).</summary>
    string NodeType,

    /// <summary>Human-readable label shown on the canvas.</summary>
    string Label,

    /// <summary>Goal prompt pre-filled into the node's GoalPrompt field; may be null.</summary>
    string? GoalPrompt
);

/// <summary>A directed connection between two generated nodes.</summary>
public sealed record GeneratedEdge(
    /// <summary>Id of the source node (from <see cref="GeneratedNode.Id"/>).</summary>
    string SourceNodeId,

    /// <summary>Id of the target node.</summary>
    string TargetNodeId
);
