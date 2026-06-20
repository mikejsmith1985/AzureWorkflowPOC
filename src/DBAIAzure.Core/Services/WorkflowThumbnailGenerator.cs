// Generates an inline SVG thumbnail for a WorkflowDefinition — one rect per node,
// one line per edge. Used on gallery cards so users can identify a workflow at a glance.

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using System.Text;

namespace DBAIAzure.Core.Services;

/// <summary>
/// Produces a compact inline SVG string that schematically represents a workflow's node layout:
/// colour-coded rectangles (one per node) connected by directional lines (one per edge).
/// The thumbnail uses a fixed 200 × 100 viewBox and a transparent background so it
/// renders correctly on any gallery card background colour.
/// </summary>
public sealed class WorkflowThumbnailGenerator : IWorkflowThumbnailGenerator
{
    private const int MaxNodes      = 50;
    private const double ViewWidth  = 200.0;
    private const double ViewHeight = 100.0;
    private const double NodeWidth  = 18.0;
    private const double NodeHeight = 10.0;
    private const double NodeRx     = 3.0;   // border-radius
    private const double Margin     = 12.0;

    /// <inheritdoc/>
    public string? GenerateSvg(WorkflowDefinition workflow)
    {
        try
        {
            if (workflow.Nodes.Count == 0)
                return null;

            var visibleNodes = workflow.Nodes.Take(MaxNodes).ToList();
            var positions    = ComputePositions(visibleNodes);
            var svg          = new StringBuilder();

            svg.Append($"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {ViewWidth:F0} {ViewHeight:F0}">""");

            // Draw edges first so nodes render on top.
            AppendEdges(svg, visibleNodes, workflow.Edges, positions);
            AppendNodes(svg, visibleNodes, positions);

            svg.Append("</svg>");
            return svg.ToString();
        }
        catch
        {
            // Article IX (Error Handling): any generation failure returns null silently.
            return null;
        }
    }

    private static Dictionary<string, (double CenterX, double CenterY)> ComputePositions(
        IReadOnlyList<WorkflowNode> nodes)
    {
        // Spread nodes evenly left-to-right across the fixed viewBox.
        var positions = new Dictionary<string, (double, double)>();
        var totalAvailableWidth = ViewWidth - 2 * Margin - NodeWidth;
        var stepX = nodes.Count > 1 ? totalAvailableWidth / (nodes.Count - 1) : 0;
        var centerY = ViewHeight / 2.0;

        for (var nodeIndex = 0; nodeIndex < nodes.Count; nodeIndex++)
        {
            var centerX = Margin + NodeWidth / 2.0 + nodeIndex * stepX;
            positions[nodes[nodeIndex].Id] = (centerX, centerY);
        }

        return positions;
    }

    private static void AppendEdges(
        StringBuilder svg,
        IReadOnlyList<WorkflowNode> visibleNodes,
        IReadOnlyList<WorkflowEdge> edges,
        Dictionary<string, (double CenterX, double CenterY)> positions)
    {
        var visibleIds = visibleNodes.Select(node => node.Id).ToHashSet();

        foreach (var edge in edges)
        {
            if (!visibleIds.Contains(edge.SourceNodeId) || !visibleIds.Contains(edge.TargetNodeId))
                continue;

            if (!positions.TryGetValue(edge.SourceNodeId, out var source) ||
                !positions.TryGetValue(edge.TargetNodeId, out var target))
                continue;

            svg.Append(
                $"""<line x1="{source.CenterX:F1}" y1="{source.CenterY:F1}" x2="{target.CenterX:F1}" y2="{target.CenterY:F1}" stroke="#6b7280" stroke-width="1" />""");
        }
    }

    private static void AppendNodes(
        StringBuilder svg,
        IReadOnlyList<WorkflowNode> nodes,
        Dictionary<string, (double CenterX, double CenterY)> positions)
    {
        foreach (var node in nodes)
        {
            if (!positions.TryGetValue(node.Id, out var position))
                continue;

            var fill = node.NodeType switch
            {
                WorkflowNodeType.Trigger       => "#10b981",  // emerald
                WorkflowNodeType.AgenticReason => "#f59e0b",  // amber
                WorkflowNodeType.HumanApproval => "#a855f7",  // purple
                _                              => "#06b6d4",  // cyan
            };

            var rectX = position.CenterX - NodeWidth  / 2.0;
            var rectY = position.CenterY - NodeHeight / 2.0;

            svg.Append(
                $"""<rect x="{rectX:F1}" y="{rectY:F1}" width="{NodeWidth:F0}" height="{NodeHeight:F0}" rx="{NodeRx:F0}" fill="{fill}" />""");
        }
    }
}
