// Generates an inline SVG thumbnail for a WorkflowDefinition — one circle per node,
// one line per edge. Used on gallery cards so users can identify a workflow at a glance.

using DBAIAzure.Core.Models;
using System.Text;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Produces a compact inline SVG string that visually represents a workflow:
/// - Warm amber circles for AgenticReason nodes
/// - Purple circles for HumanApproval nodes
/// - Cyan circles for all function-type nodes
/// - Grey lines between connected nodes
/// Capped at 50 nodes to keep thumbnail generation fast on large workflows.
/// </summary>
public sealed class WorkflowThumbnailGenerator
{
    private const int MaxNodes       = 50;
    private const int ThumbnailWidth = 240;
    private const int ThumbnailHeight = 120;
    private const int NodeRadius     = 8;
    private const int EdgeStrokeWidth = 1;

    /// <summary>
    /// Generates an SVG thumbnail string for the supplied workflow definition.
    /// Returns an empty string when the workflow has no nodes.
    /// </summary>
    /// <param name="workflow">The workflow whose topology should be visualised.</param>
    /// <returns>Inline SVG markup suitable for embedding directly in HTML.</returns>
    public string Generate(WorkflowDefinition workflow)
    {
        if (workflow.Nodes.Count == 0)
            return string.Empty;

        // Cap at MaxNodes to bound generation time.
        var visibleNodes = workflow.Nodes.Take(MaxNodes).ToList();

        var (viewBox, nodePositions) = ComputeLayout(visibleNodes, workflow.Edges);
        var svg = new StringBuilder();
        svg.Append($"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="{viewBox}" width="{ThumbnailWidth}" height="{ThumbnailHeight}" style="background:#111827;border-radius:4px">""");

        // Draw edges first so nodes render on top.
        AppendEdges(svg, visibleNodes, workflow.Edges, nodePositions);
        AppendNodes(svg, visibleNodes, nodePositions);

        svg.Append("</svg>");
        return svg.ToString();
    }

    private static (string viewBox, Dictionary<string, (double X, double Y)> positions)
        ComputeLayout(
            IReadOnlyList<WorkflowNode> nodes,
            IReadOnlyList<WorkflowEdge> edges)
    {
        // Simple left-to-right layout based on node index.
        var positions = new Dictionary<string, (double X, double Y)>();
        var margin = 20.0;
        var totalWidth = ThumbnailWidth - 2 * margin;
        var stepX = nodes.Count > 1 ? totalWidth / (nodes.Count - 1) : 0;
        var centerY = ThumbnailHeight / 2.0;

        for (var index = 0; index < nodes.Count; index++)
            positions[nodes[index].Id] = (margin + index * stepX, centerY);

        // Compute bounding box for viewBox.
        var minX = positions.Values.Min(p => p.X) - NodeRadius - 4;
        var minY = positions.Values.Min(p => p.Y) - NodeRadius - 4;
        var maxX = positions.Values.Max(p => p.X) + NodeRadius + 4;
        var maxY = positions.Values.Max(p => p.Y) + NodeRadius + 4;
        var viewBox = $"{minX:F0} {minY:F0} {(maxX - minX):F0} {(maxY - minY):F0}";

        return (viewBox, positions);
    }

    private static void AppendEdges(
        StringBuilder svg,
        IReadOnlyList<WorkflowNode> visibleNodes,
        IReadOnlyList<WorkflowEdge> edges,
        Dictionary<string, (double X, double Y)> positions)
    {
        var visibleNodeIds = visibleNodes.Select(node => node.Id).ToHashSet();

        foreach (var edge in edges)
        {
            if (!visibleNodeIds.Contains(edge.SourceNodeId) ||
                !visibleNodeIds.Contains(edge.TargetNodeId))
                continue;

            if (!positions.TryGetValue(edge.SourceNodeId, out var source) ||
                !positions.TryGetValue(edge.TargetNodeId, out var target))
                continue;

            svg.Append(
                $"""<line x1="{source.X:F1}" y1="{source.Y:F1}" x2="{target.X:F1}" y2="{target.Y:F1}" stroke="#4B5563" stroke-width="{EdgeStrokeWidth}" />""");
        }
    }

    private static void AppendNodes(
        StringBuilder svg,
        IReadOnlyList<WorkflowNode> nodes,
        Dictionary<string, (double X, double Y)> positions)
    {
        foreach (var node in nodes)
        {
            if (!positions.TryGetValue(node.Id, out var position))
                continue;

            var fill = node.NodeType switch
            {
                WorkflowNodeType.AgenticReason => "#F59E0B",   // amber
                WorkflowNodeType.HumanApproval => "#A855F7",   // purple
                _                              => "#22D3EE",   // cyan
            };

            // Amber ring if unconfigured.
            if (!node.IsConfigured)
                svg.Append(
                    $"""<circle cx="{position.X:F1}" cy="{position.Y:F1}" r="{NodeRadius + 3}" fill="none" stroke="#F59E0B" stroke-width="1.5" />""");

            svg.Append(
                $"""<circle cx="{position.X:F1}" cy="{position.Y:F1}" r="{NodeRadius}" fill="{fill}" />""");
        }
    }
}
