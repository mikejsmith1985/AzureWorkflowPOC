// Enforces structural invariants for workflow definitions before persistence or execution.

using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Validation;

/// <summary>
/// Default implementation of <see cref="IWorkflowValidator"/>. Enforces three rules:
/// VAL-001 — every workflow needs exactly one Trigger node before it can run;
/// VAL-002 — more than one Trigger is not allowed;
/// VAL-003 — no step may be completely isolated (unconnected) from the rest of the workflow.
/// </summary>
public sealed class WorkflowValidator : IWorkflowValidator
{
    /// <inheritdoc/>
    public IReadOnlyList<string> Validate(WorkflowDefinition definition)
    {
        var messages = new List<string>();

        var triggerCount = definition.Nodes.Count(n => n.NodeType == WorkflowNodeType.Trigger);

        // VAL-001: workflow has no starting trigger.
        if (triggerCount == 0)
            messages.Add("Add a starting trigger to run this workflow.");

        // VAL-002: workflow has more than one starting trigger.
        if (triggerCount > 1)
            messages.Add("A workflow may contain only one starting trigger. Remove the extra trigger before saving.");

        // VAL-003: one or more nodes have no connections at all (island nodes).
        // Only applies when there is more than one node — a single-node workflow is always isolated.
        var connectedNodeIds = definition.Edges
            .SelectMany(edge => new[] { edge.SourceNodeId, edge.TargetNodeId })
            .ToHashSet();

        var hasIslandNodes = definition.Nodes.Count > 1
                          && definition.Nodes.Any(node => !connectedNodeIds.Contains(node.Id));

        if (hasIslandNodes)
            messages.Add("One or more steps are not connected to anything. Connect all steps before running.");

        return messages.AsReadOnly();
    }
}
