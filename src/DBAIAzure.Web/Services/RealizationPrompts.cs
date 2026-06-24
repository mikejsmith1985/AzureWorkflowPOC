// Builds the system prompt and per-node-type user messages that drive node realization, giving the
// LLM the plain-language intent plus enough graph context to propose executable configuration.

using System.Text;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Centralises the natural-language prompts sent to the LLM during realization. Keeping them in one
/// place lets every node type share the same voice and the same grounding rule: propose only what the
/// user's plain-language intent and the workflow graph support — never invent connectors or fields.
/// </summary>
internal static class RealizationPrompts
{
    /// <summary>Shared system prompt: the assistant turns plain-language steps into runnable config.</summary>
    public const string System =
        "You convert a plain-language workflow step into the concrete configuration a production system " +
        "needs to run it. Base every value strictly on the step's stated purpose and its connections in " +
        "the workflow — never invent data, recipients, or fields that the user did not imply. Keep wording " +
        "plain and business-readable. Produce only the requested structured fields.";

    /// <summary>User message for a Trigger node: what starts the workflow and the initial data shape.</summary>
    public static string TriggerUser(WorkflowNode node)
    {
        var builder = StartNode(node);
        builder.AppendLine("Describe in plain language the real event or input that starts this workflow, " +
            "and list the structured fields the first step will receive.");
        return builder.ToString();
    }

    /// <summary>User message for an AgenticReason node: instruction, output shape, and tools.</summary>
    public static string AgentUser(WorkflowDefinition workflow, WorkflowNode node)
    {
        var builder = StartNode(node);
        AppendNeighbours(builder, workflow, node);
        builder.AppendLine("Write the operating instruction this AI step should follow. If a downstream step " +
            "branches or transforms on its result, define the structured output fields it must produce. List " +
            "any connectors it needs as tools, choosing only from: ServiceNow, AzureDevOps, LLM, Teams.");
        return builder.ToString();
    }

    /// <summary>User message for a FunctionRoute node: one condition per outgoing path plus a default.</summary>
    public static string RouteUser(WorkflowDefinition workflow, WorkflowNode node)
    {
        var builder = StartNode(node);
        AppendNeighbours(builder, workflow, node);
        builder.AppendLine("Outgoing paths (port ids) you must route between:");
        foreach (var port in node.OutputPorts)
            builder.AppendLine($"  - {port.Id} ({port.Label})");
        builder.AppendLine("Define one condition per outgoing path and pick one path id as the default.");
        return builder.ToString();
    }

    /// <summary>User message for a FunctionTransform node: input→output field mappings.</summary>
    public static string TransformUser(WorkflowNode node)
    {
        var builder = StartNode(node);
        builder.AppendLine("Describe how the upstream fields should be mapped into the shape the next step " +
            "expects, as a list of from-field to to-field mappings.");
        return builder.ToString();
    }

    /// <summary>User message for a FunctionNotify node: connector, recipient, message.</summary>
    public static string NotifyUser(WorkflowNode node)
    {
        var builder = StartNode(node);
        builder.AppendLine("Choose the messaging connector (Teams), describe how the recipient is determined " +
            "from the workflow data, and write the message template. Never include secrets.");
        return builder.ToString();
    }

    /// <summary>User message for a FunctionData node: connector, operation, and data mappings.</summary>
    public static string DataUser(WorkflowNode node)
    {
        var builder = StartNode(node);
        builder.AppendLine("Choose the data connector (ServiceNow or AzureDevOps), state whether this step " +
            "reads or writes, and describe how workflow data maps in and out. Never include secrets.");
        return builder.ToString();
    }

    /// <summary>User message for a HumanApproval node: approver, prompt, decision options.</summary>
    public static string ApprovalUser(WorkflowNode node)
    {
        var builder = StartNode(node);
        builder.AppendLine("Describe who should approve, what they should be shown when the workflow pauses, " +
            "and at least two decision options (for example Approve and Reject).");
        return builder.ToString();
    }

    // ── Shared builders ─────────────────────────────────────────────────────────

    /// <summary>Opens a user message with the node's own plain-language fields.</summary>
    private static StringBuilder StartNode(WorkflowNode node)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Step name: {node.Label}");
        if (!string.IsNullOrWhiteSpace(node.GoalPrompt))
            builder.AppendLine($"Stated purpose: {node.GoalPrompt}");
        if (!string.IsNullOrWhiteSpace(node.InputLabel))
            builder.AppendLine($"Receives: {node.InputLabel}");
        if (!string.IsNullOrWhiteSpace(node.OutputLabel))
            builder.AppendLine($"Produces: {node.OutputLabel}");
        builder.AppendLine();
        return builder;
    }

    /// <summary>Appends the upstream and downstream step names so the model understands the data flow.</summary>
    private static void AppendNeighbours(StringBuilder builder, WorkflowDefinition workflow, WorkflowNode node)
    {
        var nodesById = workflow.Nodes.ToDictionary(candidate => candidate.Id);

        var upstream = workflow.Edges
            .Where(edge => edge.TargetNodeId == node.Id)
            .Select(edge => nodesById.TryGetValue(edge.SourceNodeId, out var source) ? source.Label : null)
            .Where(label => label is not null)
            .ToList();

        var downstream = workflow.Edges
            .Where(edge => edge.SourceNodeId == node.Id)
            .Select(edge => nodesById.TryGetValue(edge.TargetNodeId, out var target) ? target.Label : null)
            .Where(label => label is not null)
            .ToList();

        if (upstream.Count > 0)
            builder.AppendLine($"Comes after: {string.Join(", ", upstream)}");
        if (downstream.Count > 0)
            builder.AppendLine($"Feeds into: {string.Join(", ", downstream)}");
        if (upstream.Count > 0 || downstream.Count > 0)
            builder.AppendLine();
    }
}
