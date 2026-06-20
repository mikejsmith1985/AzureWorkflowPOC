// Manages save, load, duplicate, and auto-save for the Visual Workflow Builder.
// Single service instance shared across the builder page and toolbar.

using DBAIAzure.Core.Exceptions;
using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Coordinates persistence for the Visual Workflow Builder:
/// - Saves via <see cref="IWorkflowRepository"/> (upsert with thumbnail)
/// - Loads a workflow by ID
/// - Duplicates with " (copy)" suffix
/// - Deletes with existence check
/// - Auto-saves on a 60-second debounce; exposes <see cref="LastSavedAt"/> for the toolbar
/// </summary>
public sealed class WorkflowBuilderService : IDisposable
{
    private static readonly TimeSpan AutoSaveInterval = TimeSpan.FromSeconds(60);

    private readonly IWorkflowRepository _repository;
    private readonly IWorkflowThumbnailGenerator _thumbnailGenerator;
    private readonly IWorkflowValidator _validator;
    private readonly ILogger<WorkflowBuilderService> _logger;

    private System.Threading.Timer? _autoSaveTimer;
    private Func<Task<WorkflowDefinition?>>? _autoSaveWorkflowGetter;

    /// <summary>UTC instant of the most recent successful save; null if never saved.</summary>
    public DateTimeOffset? LastSavedAt { get; private set; }

    /// <summary>Raised when auto-save or manual save completes; carries the saved-at timestamp.</summary>
    public event Action<DateTimeOffset>? WorkflowSaved;

    /// <summary>
    /// Initialises the service with the workflow repository, thumbnail generator, validator, and logger.
    /// </summary>
    public WorkflowBuilderService(
        IWorkflowRepository repository,
        IWorkflowThumbnailGenerator thumbnailGenerator,
        IWorkflowValidator validator,
        ILogger<WorkflowBuilderService> logger)
    {
        _repository         = repository;
        _thumbnailGenerator = thumbnailGenerator;
        _validator          = validator;
        _logger             = logger;
    }

    /// <summary>
    /// Saves the workflow (upsert). Generates a thumbnail before persisting.
    /// Fires <see cref="WorkflowSaved"/> on success.
    /// </summary>
    /// <param name="workflow">The current state of the workflow to persist.</param>
    /// <param name="cancellationToken">Token to cancel the save if the component is disposed.</param>
    /// <returns>The saved workflow with the updated thumbnail and LastModifiedAt.</returns>
    public async Task<WorkflowDefinition> SaveAsync(
        WorkflowDefinition workflow,
        CancellationToken cancellationToken = default)
    {
        // Gate: validate structural invariants before touching storage.
        var validationMessages = _validator.Validate(workflow);
        if (validationMessages.Count > 0)
            throw new WorkflowValidationException(validationMessages);

        var utcNow    = DateTimeOffset.UtcNow;
        var withStamp = workflow with { LastModifiedAt = utcNow };

        // Generate a thumbnail and attach it if successful; proceed without one on failure (null).
        var thumbnailSvg = _thumbnailGenerator.GenerateSvg(withStamp);
        var toSave       = thumbnailSvg is not null
            ? withStamp with { ThumbnailSvg = thumbnailSvg }
            : withStamp;

        await _repository.SaveAsync(toSave, cancellationToken).ConfigureAwait(false);

        LastSavedAt = utcNow;
        WorkflowSaved?.Invoke(utcNow);
        _logger.LogInformation("Workflow '{Name}' saved at {SavedAt}", toSave.Name, utcNow);

        return toSave;
    }

    /// <summary>
    /// Loads a workflow by ID. Returns null when the ID is not found.
    /// </summary>
    public Task<WorkflowDefinition?> LoadAsync(Guid id, string ownerId, CancellationToken cancellationToken = default)
        => _repository.GetAsync(id, ownerId, cancellationToken);

    /// <summary>
    /// Returns all workflows for the given owner. Used by the entry-choice modal to decide
    /// whether to show the first-run welcome screen.
    /// </summary>
    public Task<IReadOnlyList<WorkflowDefinition>> ListByOwnerAsync(string ownerId, CancellationToken cancellationToken = default)
        => _repository.ListByOwnerAsync(ownerId, cancellationToken);

    /// <summary>
    /// Creates a copy of the workflow with " (copy)" appended to the name (idempotent).
    /// The copy gets a new ID and reset timestamps.
    /// </summary>
    public async Task<WorkflowDefinition> DuplicateAsync(
        WorkflowDefinition original,
        CancellationToken cancellationToken = default)
    {
        var duplicateName = original.Name.EndsWith(" (copy)", StringComparison.OrdinalIgnoreCase)
            ? original.Name
            : $"{original.Name} (copy)";

        var utcNow    = DateTimeOffset.UtcNow;
        var duplicate = original with
        {
            Id             = Guid.NewGuid(),
            Name           = duplicateName,
            CreatedAt      = utcNow,
            LastModifiedAt = utcNow,
        };

        return await SaveAsync(duplicate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the workflow with the given ID. Returns false when the ID does not exist.
    /// </summary>
    public async Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken cancellationToken = default)
    {
        // Verify the workflow exists before attempting deletion.
        var existing = await _repository.GetAsync(id, ownerId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return false;

        await _repository.DeleteAsync(id, ownerId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Starts the 60-second auto-save debounce.
    /// <paramref name="workflowGetter"/> is called when the timer fires to obtain the current state.
    /// </summary>
    public void StartAutoSave(Func<Task<WorkflowDefinition?>> workflowGetter)
    {
        _autoSaveWorkflowGetter = workflowGetter;
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = new System.Threading.Timer(
            OnAutoSaveTimerElapsed,
            state: null,
            dueTime: AutoSaveInterval,
            period: AutoSaveInterval);
    }

    /// <summary>Stops the auto-save timer without triggering a final save.</summary>
    public void StopAutoSave()
    {
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;
    }

    private void OnAutoSaveTimerElapsed(object? _)
    {
        if (_autoSaveWorkflowGetter is null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                var workflow = await _autoSaveWorkflowGetter().ConfigureAwait(false);
                if (workflow is not null)
                    await SaveAsync(workflow).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-save failed.");
            }
        });
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _autoSaveTimer?.Dispose();
    }
}
