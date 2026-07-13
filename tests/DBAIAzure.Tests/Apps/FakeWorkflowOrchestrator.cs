// Fakes for monitoring-service unit tests: orchestrator + workflow repository (feature 013, US3).
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Tests.Apps;

/// <summary>Records StartRunAsync calls and returns deterministic run ids; other members no-op.</summary>
internal sealed class FakeWorkflowOrchestrator : IWorkflowExecutionOrchestrator
{
    public List<(Guid workflowId, string input)> Started { get; } = new();

    public event Action<string>? RunUpdated;

    public Task<string> StartRunAsync(WorkflowDefinition workflow, string inputDescription, CancellationToken ct = default)
    {
        Started.Add((workflow.Id, inputDescription));
        RunUpdated?.Invoke($"run-{Started.Count}");
        return Task.FromResult($"run-{Started.Count}");
    }

    public WorkflowExecutionRun? GetRun(string runId) => null;
    public void RequestStop(string runId) { }
    public void SubmitApproval(string runId, bool approved) { }
    public void RehydratePausedRun(WorkflowRunRecord record) { }
}

/// <summary>Returns a single configured workflow by id, or null (to simulate a deleted link).</summary>
internal sealed class FakeWorkflowRepository : IWorkflowRepository
{
    private readonly WorkflowDefinition? _workflow;

    public FakeWorkflowRepository(WorkflowDefinition? workflow) => _workflow = workflow;

    public Task<WorkflowDefinition?> GetAsync(Guid id, string ownerId, CancellationToken ct = default)
        => Task.FromResult(_workflow is not null && _workflow.Id == id && _workflow.OwnerId == ownerId ? _workflow : null);

    public Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult(_workflow is not null && _workflow.Id == id ? _workflow : null);

    public Task<Guid> SaveAsync(WorkflowDefinition workflow, CancellationToken ct = default)
        => Task.FromResult(workflow.Id);

    public Task<IReadOnlyList<WorkflowDefinition>> ListByOwnerAsync(string ownerId, CancellationToken ct = default)
        => Task.FromResult((IReadOnlyList<WorkflowDefinition>)(_workflow is null ? Array.Empty<WorkflowDefinition>() : new[] { _workflow }));

    public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct = default) => Task.FromResult(true);

    public Task<bool> ExistsAsync(string name, string ownerId, CancellationToken ct = default) => Task.FromResult(false);
}
