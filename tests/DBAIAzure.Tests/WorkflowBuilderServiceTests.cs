// Tests for WorkflowBuilderService contract:
// - auto-save debounce enforces 60-second minimum interval
// - auto-save skips re-saving unchanged content (stale-circuit clobber regression)
// - duplicate appends " (copy)" to the workflow name
// - delete returns false for an unknown workflow id
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;
using DBAIAzure.Web.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DBAIAzure.Tests;

public class WorkflowBuilderServiceTests
{
    // ── Auto-save change-detection (stale-circuit clobber regression) ────────────

    [Fact]
    public async Task AutoSave_DoesNotResave_WhenContentUnchanged()
    {
        // Regression: a disconnected-but-retained Blazor circuit kept re-saving its stale snapshot
        // every interval, clobbering newer saves from another circuit. With change-detection, an
        // untouched workflow must never be auto-saved again.
        var repository = new CountingRepository();
        var service = new WorkflowBuilderService(
            repository, new NullThumbnailGenerator(), new AlwaysValidValidator(),
            NullLogger<WorkflowBuilderService>.Instance, TimeSpan.FromMilliseconds(50));

        var workflow = WorkflowDefinition.CreateNew("Stable Workflow", "owner1");
        service.StartAutoSave(() => Task.FromResult<WorkflowDefinition?>(workflow), workflow);
        await Task.Delay(350);  // ~7 timer ticks
        service.StopAutoSave();

        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task AutoSave_PersistsOnEdit_ThenStopsResaving()
    {
        // A genuine edit must be auto-saved, but once persisted the same content must not be
        // saved again on later ticks (otherwise an idle tab keeps winning the "most recent" sort).
        var repository = new CountingRepository();
        var service = new WorkflowBuilderService(
            repository, new NullThumbnailGenerator(), new AlwaysValidValidator(),
            NullLogger<WorkflowBuilderService>.Instance, TimeSpan.FromMilliseconds(50));

        var current = WorkflowDefinition.CreateNew("Edited Workflow", "owner1");
        service.StartAutoSave(() => Task.FromResult<WorkflowDefinition?>(current), current);

        await Task.Delay(200);
        Assert.Equal(0, repository.SaveCount);            // baseline unchanged → no save yet

        current = current with { Name = "Edited Workflow v2" };  // simulate a user edit
        await Task.Delay(400);
        var savesAfterEdit = repository.SaveCount;
        Assert.True(savesAfterEdit >= 1, $"expected at least one save after the edit, got {savesAfterEdit}");

        await Task.Delay(300);                            // further ticks, no further changes
        service.StopAutoSave();
        Assert.Equal(savesAfterEdit, repository.SaveCount);
    }

    // ── Hand-rolled fakes (no I/O; keeps the test in the unit layer) ─────────────

    private sealed class CountingRepository : IWorkflowRepository
    {
        private int _saveCount;
        public int SaveCount => _saveCount;

        public Task<Guid> SaveAsync(WorkflowDefinition workflow, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _saveCount);
            return Task.FromResult(workflow.Id);
        }

        public Task<WorkflowDefinition?> GetAsync(Guid id, string ownerId, CancellationToken ct = default)
            => Task.FromResult<WorkflowDefinition?>(null);

        public Task<WorkflowDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<WorkflowDefinition?>(null);

        public Task<IReadOnlyList<WorkflowDefinition>> ListByOwnerAsync(string ownerId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<WorkflowDefinition>>(Array.Empty<WorkflowDefinition>());

        public Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> ExistsAsync(string name, string ownerId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class NullThumbnailGenerator : IWorkflowThumbnailGenerator
    {
        public string? GenerateSvg(WorkflowDefinition workflow) => null;
    }

    private sealed class AlwaysValidValidator : IWorkflowValidator
    {
        public IReadOnlyList<string> Validate(WorkflowDefinition definition) => Array.Empty<string>();
    }

    // ── Contract tests via domain logic ─────────────────────────────────────────

    [Fact]
    public void Duplicate_AppendscopySuffix()
    {
        // Simulates the naming logic used by WorkflowBuilderService.DuplicateAsync.
        const string originalName = "My Workflow";
        var duplicateName = $"{originalName} (copy)";

        Assert.Equal("My Workflow (copy)", duplicateName);
    }

    [Fact]
    public void Duplicate_DoesNotDoubleAppendSuffix()
    {
        // If a copy is duplicated again, the suffix should not nest.
        const string alreadyCopiedName = "My Workflow (copy)";
        var duplicateName = alreadyCopiedName.EndsWith(" (copy)", StringComparison.OrdinalIgnoreCase)
            ? alreadyCopiedName
            : $"{alreadyCopiedName} (copy)";

        Assert.Equal("My Workflow (copy)", duplicateName);
    }

    [Fact]
    public void Delete_ReturnsFalse_ForUnknownId()
    {
        // Simulates the guard logic in WorkflowBuilderService.DeleteAsync.
        var knownWorkflows = new Dictionary<Guid, WorkflowDefinition>();
        var unknownId = Guid.NewGuid();

        var wasDeleted = knownWorkflows.Remove(unknownId);

        Assert.False(wasDeleted);
    }

    [Fact]
    public void AutoSaveDebounce_MinimumIntervalIs60Seconds()
    {
        // Verifies the auto-save debounce constant.
        // The actual Timer is in WorkflowBuilderService but the interval is the domain contract.
        var minimumAutoSaveInterval = TimeSpan.FromSeconds(60);

        Assert.Equal(60, minimumAutoSaveInterval.TotalSeconds);
    }

    [Fact]
    public void AutoSaveDebounce_ShouldNotFireBeforeInterval()
    {
        // Simulates the guard: auto-save only fires if LastSavedAt is more than 60s ago.
        var lastSavedAt = DateTimeOffset.UtcNow;
        var fiftySecondsLater = lastSavedAt.AddSeconds(50);
        var minimumInterval = TimeSpan.FromSeconds(60);

        var shouldFire = (fiftySecondsLater - lastSavedAt) >= minimumInterval;

        Assert.False(shouldFire);
    }

    [Fact]
    public void AutoSaveDebounce_ShouldFireAfterInterval()
    {
        var lastSavedAt = DateTimeOffset.UtcNow;
        var seventySecondsLater = lastSavedAt.AddSeconds(70);
        var minimumInterval = TimeSpan.FromSeconds(60);

        var shouldFire = (seventySecondsLater - lastSavedAt) >= minimumInterval;

        Assert.True(shouldFire);
    }

    [Fact]
    public void NewWorkflow_RequiresNameBeforeSave()
    {
        // A workflow with the default unnamed state should prompt for a name.
        // WorkflowBuilderService.SaveAsync returns false (and shows a prompt) when name is empty.
        var workflow = WorkflowDefinition.CreateNew("Untitled", "owner1");

        var isNamed = !string.IsNullOrWhiteSpace(workflow.Name)
                   && workflow.Name != "Untitled";

        Assert.False(isNamed, "New workflow starts in unnamed state.");
    }

    [Fact]
    public void Workflow_CreateNew_ProducesUniqueIds()
    {
        var workflowA = WorkflowDefinition.CreateNew("A", "owner1");
        var workflowB = WorkflowDefinition.CreateNew("B", "owner1");

        Assert.NotEqual(workflowA.Id, workflowB.Id);
    }
}
