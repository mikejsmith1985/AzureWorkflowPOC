// EF Core entity representing a saved workflow definition in the WorkflowDefinitions table.

namespace DBAIAzure.Storage.Entities;

/// <summary>
/// EF Core entity for the WorkflowDefinitions SQLite table. Topology, settings, and chat history
/// are stored as JSON TEXT blob columns — same pattern as ConnectorConfigRecord.ConfigJson.
/// The topology is always read/written atomically.
/// </summary>
public sealed class WorkflowDefinitionRecord
{
    /// <summary>
    /// Primary key — uniquely identifies this workflow definition across all users.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Human-readable display name for the workflow, chosen by the owner.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Identifier of the user who created and owns this workflow definition.
    /// </summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized <c>IReadOnlyList&lt;WorkflowNode&gt;</c> representing every node (step)
    /// placed on the canvas. Stored atomically so partial topology writes cannot occur.
    /// </summary>
    public string NodesJson { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized <c>IReadOnlyList&lt;WorkflowEdge&gt;</c> representing directed connections
    /// between nodes. Stored alongside <see cref="NodesJson"/> in a single atomic write.
    /// </summary>
    public string EdgesJson { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized <c>WorkflowSettings</c> carrying global pipeline options such as timeout,
    /// retry policy, and target connector identifiers.
    /// </summary>
    public string SettingsJson { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized <c>IReadOnlyList&lt;WorkflowChatMessage&gt;</c> containing the full
    /// conversation history between the user and the AI assistant for this workflow session.
    /// </summary>
    public string ChatHistoryJson { get; set; } = string.Empty;

    /// <summary>
    /// The AI-generated C# or YAML code emitted for this workflow definition, if generation has
    /// been triggered. Null until the user requests code generation.
    /// </summary>
    public string? GeneratedCode { get; set; }

    /// <summary>
    /// An SVG thumbnail snapshot of the canvas layout, persisted so list views can render a
    /// preview without loading the full topology. Null until a snapshot is captured.
    /// </summary>
    public string? ThumbnailSvg { get; set; }

    /// <summary>
    /// UTC instant at which this record was first persisted. Set once on insert and never updated.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// UTC instant of the most recent write to any property on this record. Updated on every save.
    /// </summary>
    public DateTimeOffset LastModifiedAt { get; set; } = DateTimeOffset.UtcNow;
}
