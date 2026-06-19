// Represents the complete, persisted definition of a user-authored workflow — nodes, edges,
// settings, chat history, and generated code — stored in the personal gallery.

namespace DBAIAzure.Core.Models;

/// <summary>
/// Immutable snapshot of a workflow as authored by a single user.
/// A <see cref="WorkflowDefinition"/> is the unit of persistence in the personal gallery:
/// it carries everything needed to reconstruct the visual canvas, replay the chat session,
/// and re-execute the last generated code without any additional lookups.
/// </summary>
public sealed record WorkflowDefinition
{
    /// <summary>
    /// Stable, globally unique identifier assigned at creation and never changed.
    /// Used as the primary key in storage and as the route parameter in the gallery API.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Human-readable display name provided by the owner.
    /// Shown on the gallery card and in the canvas title bar.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Identifier of the authenticated user who created this workflow.
    /// The gallery is personal-only: queries always filter by <see cref="OwnerId"/>
    /// so one user can never see or modify another user's workflows.
    /// </summary>
    public required string OwnerId { get; init; }

    /// <summary>
    /// Ordered collection of canvas nodes that make up the workflow graph.
    /// An empty list represents a blank canvas immediately after creation.
    /// </summary>
    public required IReadOnlyList<WorkflowNode> Nodes { get; init; }

    /// <summary>
    /// Directed edges that connect <see cref="Nodes"/>.
    /// An empty list is valid; it means the nodes have not yet been wired together.
    /// </summary>
    public required IReadOnlyList<WorkflowEdge> Edges { get; init; }

    /// <summary>
    /// Runtime and rendering configuration that applies to the whole workflow.
    /// A default-constructed <see cref="WorkflowSettings"/> is used for new workflows.
    /// </summary>
    public required WorkflowSettings Settings { get; init; }

    /// <summary>
    /// Full chronological history of the chat session between the user and the code-generation
    /// assistant. Persisted so the session can be resumed across browser reloads.
    /// </summary>
    public required IReadOnlyList<WorkflowChatMessage> ChatHistory { get; init; }

    /// <summary>
    /// The most recent C# code produced by the chat assistant for this workflow.
    /// <c>null</c> when the user has not yet triggered code generation.
    /// Stored here so the code panel can be restored without re-generating.
    /// </summary>
    public string? GeneratedCode { get; init; }

    /// <summary>
    /// UTC instant at which this workflow was first created.
    /// Immutable after <see cref="CreateNew"/> — never updated on subsequent saves.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// UTC instant of the most recent save.
    /// Updated on every write so the gallery can sort by "recently edited."
    /// </summary>
    public required DateTimeOffset LastModifiedAt { get; init; }

    /// <summary>
    /// Inline SVG string rendered as the visual thumbnail on the gallery card.
    /// <c>null</c> until the canvas produces its first snapshot on save.
    /// </summary>
    public string? ThumbnailSvg { get; init; }

    /// <summary>
    /// Creates a brand-new, empty <see cref="WorkflowDefinition"/> ready to be opened in the
    /// visual builder. All collections are empty, settings are at their defaults, and both
    /// timestamps are set to the current UTC instant so the record is immediately gallery-ready.
    /// </summary>
    /// <param name="name">Display name chosen by the user for this workflow.</param>
    /// <param name="ownerId">Identifier of the authenticated user creating the workflow.</param>
    /// <returns>A fully initialised <see cref="WorkflowDefinition"/> with a new <see cref="Id"/>.</returns>
    public static WorkflowDefinition CreateNew(string name, string ownerId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Workflow name must not be empty.", nameof(name));

        if (string.IsNullOrWhiteSpace(ownerId))
            throw new ArgumentException("Owner identifier must not be empty.", nameof(ownerId));

        var utcNow = DateTimeOffset.UtcNow;

        return new WorkflowDefinition
        {
            Id             = Guid.NewGuid(),
            Name           = name,
            OwnerId        = ownerId,
            Nodes          = Array.Empty<WorkflowNode>(),
            Edges          = Array.Empty<WorkflowEdge>(),
            Settings       = new WorkflowSettings(),
            ChatHistory    = Array.Empty<WorkflowChatMessage>(),
            GeneratedCode  = null,
            CreatedAt      = utcNow,
            LastModifiedAt = utcNow,
            ThumbnailSvg   = null,
        };
    }
}
