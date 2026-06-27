// Contract for the LLM-driven step that proposes executable per-node configuration and applies
// user-accepted proposals to a workflow.

using DBAIAzure.Core.Models;
using DBAIAzure.Core.Models.NodeConfig;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Uses the LLM to propose executable per-node configuration for a plain-language workflow, and
/// applies user-accepted proposals. Proposing is read-only (no mutation, no save); acceptance is an
/// explicit, separate, deterministic step. See contracts/realization-service.md (C1).
/// </summary>
public interface IWorkflowRealizationService
{
    /// <summary>
    /// Proposes configuration for every node that needs it, in graph order. Each proposal is surfaced
    /// to <paramref name="onProposal"/> as it is produced so the UI can show per-node progress. Does
    /// not mutate <paramref name="workflow"/>.
    /// </summary>
    Task<IReadOnlyList<RealizationProposal>> ProposeAllAsync(
        WorkflowDefinition workflow,
        Action<RealizationProposal> onProposal,
        CancellationToken cancellationToken = default);

    /// <summary>Proposes configuration for a single node only — never touches other nodes.</summary>
    Task<RealizationProposal> ProposeNodeAsync(
        WorkflowDefinition workflow,
        string nodeId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a new workflow with <paramref name="acceptedConfig"/> applied to the proposal's node:
    /// writes it into that node's FunctionConfig, sets IsConfigured = true, preserves Label/GoalPrompt,
    /// and records the node's intent hash in <see cref="WorkflowSettings.RealizationProvenance"/>.
    /// Pure and deterministic — no LLM call; only the one node and settings change. Throws
    /// <see cref="ArgumentException"/> if <paramref name="acceptedConfig"/> is intrinsically invalid.
    /// </summary>
    WorkflowDefinition AcceptProposal(
        WorkflowDefinition workflow,
        RealizationProposal proposal,
        RealizedNodeConfigEnvelope acceptedConfig);
}
