// Contract for evaluating whether a workflow is production-ready.

using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Evaluates whether a workflow is production-ready: structural validity, per-node realized-config
/// validity, cross-node consistency, and connector health. Async because it performs a live connector
/// health check. See contracts/realization-service.md (C2).
/// </summary>
public interface IWorkflowReadinessService
{
    /// <summary>
    /// Produces a <see cref="WorkflowReadinessReport"/> for <paramref name="workflow"/> — overall
    /// ready/not-ready plus per-node status and plain-language reasons. Ready only when every node is
    /// <see cref="NodeRealizationStatus.Realized"/>.
    /// </summary>
    Task<WorkflowReadinessReport> EvaluateAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default);
}
