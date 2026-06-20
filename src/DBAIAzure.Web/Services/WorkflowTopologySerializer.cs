// Converts a WorkflowDefinition into a structured text representation that the LLM
// reads as its topology context when generating or refining SK process code.

using DBAIAzure.Core.Models;
using System.Text;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Serialises a <see cref="WorkflowDefinition"/> into a compact human-readable topology text
/// that the code-generation prompt uses as its primary context.
/// The output is intentionally plain text (no JSON or XML) so it is easy for the LLM to reason
/// about node relationships and data flow without parsing overhead.
/// </summary>
public sealed class WorkflowTopologySerializer
{
    /// <summary>
    /// Converts the supplied workflow into a structured text block containing:
    /// - workflow name and timeout setting
    /// - numbered node catalogue (index, label, type, goal, input/output labels)
    /// - edge routing table (source label → target label with edge label)
    /// </summary>
    /// <param name="workflow">The workflow definition to serialise.</param>
    /// <returns>
    /// A multi-line string ready to be injected into an LLM system or user prompt.
    /// </returns>
    public string Serialize(WorkflowDefinition workflow)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"Workflow: {workflow.Name}");
        builder.AppendLine($"Timeout: {workflow.Settings.ExecutionTimeoutMinutes} minutes");
        builder.AppendLine();
        AppendNodeCatalogue(builder, workflow.Nodes);
        AppendEdgeTable(builder, workflow.Nodes, workflow.Edges);

        return builder.ToString();
    }

    private static void AppendNodeCatalogue(StringBuilder builder, IReadOnlyList<WorkflowNode> nodes)
    {
        builder.AppendLine("=== Steps ===");

        if (nodes.Count == 0)
        {
            builder.AppendLine("(no steps defined)");
            return;
        }

        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            builder.AppendLine($"[{index + 1}] {node.Label} ({node.NodeType})");

            if (!string.IsNullOrWhiteSpace(node.GoalPrompt))
                builder.AppendLine($"    Goal: {node.GoalPrompt}");

            if (!string.IsNullOrWhiteSpace(node.InputLabel))
                builder.AppendLine($"    Input: {node.InputLabel}");

            if (!string.IsNullOrWhiteSpace(node.OutputLabel))
                builder.AppendLine($"    Output: {node.OutputLabel}");
        }
    }

    private static void AppendEdgeTable(
        StringBuilder builder,
        IReadOnlyList<WorkflowNode> nodes,
        IReadOnlyList<WorkflowEdge> edges)
    {
        builder.AppendLine();
        builder.AppendLine("=== Connections ===");

        if (edges.Count == 0)
        {
            builder.AppendLine("(no connections defined)");
            return;
        }

        // Build an O(1) lookup by node ID to avoid repeated linear scans.
        var nodeById = nodes.ToDictionary(node => node.Id);

        foreach (var edge in edges)
        {
            var sourceLabel = nodeById.TryGetValue(edge.SourceNodeId, out var sourceNode)
                ? sourceNode.Label
                : edge.SourceNodeId;

            var targetLabel = nodeById.TryGetValue(edge.TargetNodeId, out var targetNode)
                ? targetNode.Label
                : edge.TargetNodeId;

            builder.AppendLine($"{sourceLabel} -> {targetLabel} (label: {edge.Label})");
        }
    }
}
