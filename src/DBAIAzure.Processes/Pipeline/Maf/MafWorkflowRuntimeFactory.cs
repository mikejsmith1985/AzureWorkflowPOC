// Translates a persisted WorkflowDefinition into a live MAF Workflow (spec-019 T021) — the GA
// replacement for the SK WorkflowRuntimeBuilder. Stub for now: parity test T016 is written first.
using DBAIAzure.Core.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DBAIAzure.Processes.Pipeline.Maf;

/// <summary>
/// Builds a runtime MAF <see cref="Workflow"/> from a visual <see cref="WorkflowDefinition"/>: one
/// <see cref="Microsoft.Agents.AI.Workflows.Executor"/> per canvas node and one edge per canvas edge,
/// with <see cref="WorkflowNodeType.FunctionRoute"/> nodes wired as <b>conditional edges</b>
/// (<c>WorkflowBuilder.AddEdge(source, target, condition, label)</c>) keyed on the chosen port label —
/// the MAF 1.13 equivalent of the SK route step's port-label event routing (there is no <c>AddSwitch</c>
/// in the GA API; routing is expressed as labelled conditional edges). Replaces
/// <c>WorkflowRuntimeBuilder</c>.
/// </summary>
public sealed class MafWorkflowRuntimeFactory
{
    /// <summary>
    /// Output-port labels keyed by node id for every <see cref="WorkflowNodeType.FunctionRoute"/> node in
    /// the last <see cref="Build"/> call — the label set each route node's decision is validated against.
    /// </summary>
    public Dictionary<string, IReadOnlyList<string>> PortLabelsByNodeId { get; } = [];

    /// <summary>
    /// Builds the workflow for <paramref name="workflow"/>. The first node is the entry executor;
    /// <paramref name="chatClient"/> is the model client for agentic/route executors and
    /// <paramref name="services"/> supplies any node dependencies (notify gateway, data store).
    /// </summary>
    /// <returns>A runnable <see cref="Workflow"/> mirroring the canvas graph.</returns>
    public Workflow Build(WorkflowDefinition workflow, IChatClient chatClient, IServiceProvider? services = null)
    {
        // Implemented in US1 T018 (stateful node executors) + T021 (this translation). Parity test T016
        // is authored first and asserts node→executor mapping and port-label conditional-edge routing.
        throw new NotImplementedException(
            "MafWorkflowRuntimeFactory.Build is pending US1 (spec-019 T018/T021). Parity test T016 defines the target.");
    }
}
