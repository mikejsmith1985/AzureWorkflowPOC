// Unit tests for LegacyExampleWorkflowPurger: proves it deletes the pre-spec-021 "Support Request Flow" example
// (and its name-unique variants) while leaving the DoR workflow and the user's own workflows untouched.
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests;

public sealed class LegacyExampleWorkflowPurgerTests
{
    [Fact]
    public async Task PurgeAsync_DeletesLegacyExamples_AndKeepsOtherWorkflows()
    {
        var legacy = WorkflowDefinition.CreateNew("Example: Support Request Flow", "demo");
        var legacyVariant = WorkflowDefinition.CreateNew("Example: Support Request Flow (2)", "demo");
        var dorWorkflow = WorkflowDefinition.CreateNew("Intelligent DoR Validation Workflow", "demo");
        var userWorkflow = WorkflowDefinition.CreateNew("My Custom Flow", "demo");
        var repo = new FakeWorkflowRepository(legacy, legacyVariant, dorWorkflow, userWorkflow);

        var deletedCount = await new LegacyExampleWorkflowPurger(repo, NullLogger<LegacyExampleWorkflowPurger>.Instance)
            .PurgeAsync();

        Assert.Equal(2, deletedCount);
        Assert.Equal(new[] { legacy.Id, legacyVariant.Id }, repo.DeletedIds);
        Assert.DoesNotContain(dorWorkflow.Id, repo.DeletedIds);
        Assert.DoesNotContain(userWorkflow.Id, repo.DeletedIds);
    }

    [Fact]
    public async Task PurgeAsync_NoLegacyExamples_DeletesNothing()
    {
        var repo = new FakeWorkflowRepository(WorkflowDefinition.CreateNew("Intelligent DoR Validation Workflow", "demo"));

        var deletedCount = await new LegacyExampleWorkflowPurger(repo, NullLogger<LegacyExampleWorkflowPurger>.Instance)
            .PurgeAsync();

        Assert.Equal(0, deletedCount);
        Assert.Empty(repo.DeletedIds);
    }

    /// <summary>Minimal in-memory workflow repository that records which ids were deleted.</summary>
    private sealed class FakeWorkflowRepository : IWorkflowRepository
    {
        private readonly List<WorkflowDefinition> _workflows;
        public List<Guid> DeletedIds { get; } = new();

        public FakeWorkflowRepository(params WorkflowDefinition[] workflows) => _workflows = workflows.ToList();

        public Task<IReadOnlyList<WorkflowDefinition>> ListByOwnerAsync(string ownerId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkflowDefinition>>(_workflows.Where(w => w.OwnerId == ownerId).ToList());

        public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct = default)
        {
            DeletedIds.Add(id);
            return Task.FromResult(_workflows.RemoveAll(w => w.Id == id && w.OwnerId == ownerId) > 0);
        }

        public Task<Guid> SaveAsync(WorkflowDefinition workflow, CancellationToken ct = default) =>
            Task.FromResult(workflow.Id);
        public Task<WorkflowDefinition?> GetAsync(Guid id, string ownerId, CancellationToken ct = default) =>
            Task.FromResult<WorkflowDefinition?>(_workflows.FirstOrDefault(w => w.Id == id && w.OwnerId == ownerId));
        public Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<WorkflowDefinition?>(_workflows.FirstOrDefault(w => w.Id == id));
        public Task<bool> ExistsAsync(string name, string ownerId, CancellationToken ct = default) =>
            Task.FromResult(_workflows.Any(w => w.Name == name && w.OwnerId == ownerId));
    }
}
